using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DiscordEventService.Commands;
using DiscordEventService.Configuration;
using DiscordEventService.Jobs;
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
internal static partial class TelemetryRegistration
{
    public const string ServiceName = "discord-event-service";

    private const string NpgsqlSourceName = "Npgsql";

    // The runtime's own source for HttpClient spans; AddHttpClientInstrumentation listens to it.
    private const string HttpClientSourceName = "System.Net.Http";

    // The request histogram of ASP.NET Core ("http_server_request_duration_seconds").
    private const string HttpServerRequestDuration = "http.server.request.duration";

    // An API call here takes 50 ms to 2 s. The default boundaries of the framework step from
    // 1 s to 2.5 s, so a quantile in that range was a guess inside one bucket.
    private static readonly double[] HttpServerRequestSeconds =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.2, 0.3, 0.5, 0.75, 1, 1.5, 2, 3, 5, 10];

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
        //
        // TraceBased: a measurement made inside a sampled span keeps the trace id as an
        // exemplar. The exporter writes exemplars only to a scrape that asks for OpenMetrics
        // (Prometheus does); the plain text format is unchanged. With no trace target there is
        // no sampled span and no exemplar.
        //
        // A span Tempo does not get (IsForTempo: a database or HTTP client span with no root)
        // is still a sampled span, so a driver or HttpClient histogram can carry an exemplar
        // of a trace Tempo does not hold. The bot's own histograms and the request histogram
        // are measured under a root.
        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddMeter(NpgsqlSourceName)
            .AddMeter(BotMetrics.MeterName)
            // A view only for the one histogram of a meter the bot does not own. The bot's own
            // histograms carry their boundaries themselves (BotMetrics).
            .AddView(HttpServerRequestDuration,
                new ExplicitBucketHistogramConfiguration { Boundaries = HttpServerRequestSeconds })
            .SetExemplarFilter(ExemplarFilterType.TraceBased)
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

            // The roots outside a request: a slash command and a traced Hangfire job. The
            // redaction comes before the exporters: the processors of a provider run in order.
            tracing
                .AddSource(CommandMetrics.SourceName, TracedJobAttribute.SourceName)
                .AddAspNetCoreInstrumentation(aspNet => aspNet.Filter = context => !IsProbe(context.Request.Path))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddProcessor(new UrlTokenRedactionProcessor())
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

    // Tempo takes a span only when its trace has a real root: a request, a conversation
    // turn, a slash command or a traced job. A database or HTTP client span with no parent is
    // a trace of one span, and there are thousands of them per hour (the 5 s heartbeat, the
    // Hangfire queue poll, every Discord REST call of a backfill). The same span inside a root
    // has a parent and is kept.
    internal static bool IsForTempo(Activity activity) =>
        activity.ParentSpanId != default
        || (activity.Source.Name != NpgsqlSourceName && activity.Source.Name != HttpClientSourceName);

    // A Discord webhook URL and an interaction URL hold their secret in the path:
    // /webhooks/{id}/{token} and /interactions/{id}/{token}/callback. The runtime hides the
    // query of a client span URL, not its path, so the token segment is replaced here. The
    // health check webhook and every answer of a slash command are such calls.
    internal static string RedactUrlTokens(string url) => TokenInPath().Replace(url, "$1/REDACTED");

    [GeneratedRegex(@"(/(?:webhooks|interactions)/\d+)/[^/?#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenInPath();

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

// Rewrites the URL tags of a span before any exporter reads them: see RedactUrlTokens.
// "url.full" is the current name of the tag, "http.url" the one before it.
internal sealed class UrlTokenRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly string[] UrlTags = ["url.full", "http.url"];

    public override void OnEnd(Activity data)
    {
        foreach (var tag in UrlTags)
        {
            if (data.GetTagItem(tag) is string url)
                data.SetTag(tag, TelemetryRegistration.RedactUrlTokens(url));
        }
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
