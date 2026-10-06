using System.Diagnostics;
using System.Text;
using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DiscordEventService.Infrastructure;

// The OpenTelemetry wiring, root container only: metrics for the Prometheus scrape on /metrics
// (always on), and traces for Langfuse and Grafana Tempo (each on only when configured).
internal static class TelemetryRegistration
{
    public const string ServiceName = "discord-event-service";

    private const string NpgsqlSourceName = "Npgsql";

    // The runtime's own source for HttpClient spans; AddHttpClientInstrumentation listens to it.
    private const string HttpClientSourceName = "System.Net.Http";

    public static IServiceCollection AddBotTelemetry(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // A value that is not a URL stops the boot here, with the key in the message, and not
        // later inside the tracer provider.
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .Validate(options => options.HasValidTracesEndpoint,
                $"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpTracesEndpoint)} must be an absolute http or https URL")
            .ValidateOnStart();

        // The exporter only answers a scrape: no push, no background work, so this needs no
        // config gate. "Npgsql" is the driver's own meter (connection pool, command duration).
        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddMeter(NpgsqlSourceName)
            .AddMeter(BotMetrics.MeterName)
            .AddPrometheusExporter());

        AddTracing(services, configuration, environment);
        return services;
    }

    // Four cases. Neither target: no TracerProvider at all, so ConversationTelemetry's
    // StartActivity keeps returning null. Langfuse only: the pipeline is what it was before
    // Tempo existed. Tempo on: one provider with the HTTP and Npgsql sources added, and each
    // target behind its own filter, because every processor of a provider sees every span.
    private static void AddTracing(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var conversation = configuration.GetSection(ConversationOptions.SectionName).Get<ConversationOptions>()
            ?? new ConversationOptions();
        var telemetry = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        // An invalid endpoint is left to the options validation above, which names the key.
        if (!telemetry.HasValidTracesEndpoint || (!conversation.LangfuseConfigured && !telemetry.TracesConfigured))
            return;

        // An export that fails is silent without this: see TraceExportFailureListener.
        services.AddHostedService<TraceExportFailureListener>();

        services.AddOpenTelemetry().WithTracing(tracing =>
        {
            // langfuse.environment separates dev and prod traces inside the one shared
            // Langfuse project (both export with the same keys).
            tracing
                .ConfigureResource(resource => resource
                    .AddService(ServiceName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["langfuse.environment"] = environment.EnvironmentName.ToLowerInvariant(),
                    }))
                .AddSource(ConversationTelemetry.SourceName);

            if (!telemetry.TracesConfigured)
            {
                tracing.AddOtlpExporter(exporter => ConfigureLangfuseExporter(exporter, conversation));
                return;
            }

            tracing
                .AddAspNetCoreInstrumentation(aspNet => aspNet.Filter = context => !IsProbe(context.Request.Path))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddProcessor(new FilteredBatchActivityExportProcessor(
                    new OtlpTraceExporter(new OtlpExporterOptions
                    {
                        Endpoint = telemetry.TracesUri,
                        Protocol = OtlpExportProtocol.HttpProtobuf,
                    }),
                    IsForTempo));

            if (conversation.LangfuseConfigured)
            {
                var langfuse = new OtlpExporterOptions();
                ConfigureLangfuseExporter(langfuse, conversation);
                tracing.AddProcessor(new FilteredBatchActivityExportProcessor(new OtlpTraceExporter(langfuse), IsForLangfuse));
            }
        });
    }

    // Langfuse is an LLM trace store: it takes the conversation spans and nothing else.
    internal static bool IsForLangfuse(Activity activity) =>
        activity.Source.Name == ConversationTelemetry.SourceName;

    // Tempo takes a span only when its trace has a real root: a request or a conversation
    // turn. A database or HTTP client span with no parent is a trace of one span, and there
    // are thousands of them per hour (the 5 s heartbeat, the Hangfire queue poll, every
    // Discord REST call of a backfill). The same span inside a request or a turn has a
    // parent and is kept.
    internal static bool IsForTempo(Activity activity) =>
        activity.ParentSpanId != default
        || (activity.Source.Name != NpgsqlSourceName && activity.Source.Name != HttpClientSourceName);

    // The scrape and the Docker HEALTHCHECK: periodic, and never the request someone looks for.
    internal static bool IsProbe(PathString path) =>
        path.StartsWithSegments("/metrics") || path.StartsWithSegments("/health");

    private static void ConfigureLangfuseExporter(OtlpExporterOptions exporter, ConversationOptions conversation)
    {
        // HttpProtobuf, not gRPC — Langfuse's OTLP endpoint silently no-ops on gRPC.
        exporter.Endpoint = new Uri($"{conversation.LangfuseHost!.TrimEnd('/')}/api/public/otel/v1/traces");
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
        exporter.Headers = "Authorization=Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{conversation.LangfusePublicKey}:{conversation.LangfuseSecretKey}"));
    }
}

// A batch export processor that takes only the spans its predicate accepts. Derived from the
// SDK processor, not wrapped around it: the provider hands a processor it knows the resource
// (service name, langfuse.environment), and a wrapped one would export without it.
internal sealed class FilteredBatchActivityExportProcessor(BaseExporter<Activity> exporter, Func<Activity, bool> accepts)
    : BatchActivityExportProcessor(exporter)
{
    public override void OnEnd(Activity data)
    {
        if (accepts(data))
            base.OnEnd(data);
    }
}
