using System.Diagnostics;
using DiscordEventService.Configuration;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.Conversation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class TelemetryRegistrationTests
{
    [Fact]
    public void LangfuseProcessor_ExportsOnlyConversationSpans_WithTheProviderResource()
    {
        var otherSourceName = $"Other-{Guid.NewGuid():N}";
        using var otherSource = new ActivitySource(otherSourceName);
        var spanName = $"turn-{Guid.NewGuid():N}";
        var exporter = new CapturingExporter();

        using (var provider = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddService(TelemetryRegistration.ServiceName))
            .AddSource(ConversationTelemetry.SourceName, otherSourceName)
            .AddProcessor(new FilteredBatchActivityExportProcessor(exporter, TelemetryRegistration.IsForLangfuse))
            .Build())
        {
            using (otherSource.StartActivity("http-or-npgsql"))
            {
                // A conversation span under a foreign parent still goes to Langfuse.
                ConversationTelemetry.ActivitySource.StartActivity(spanName)?.Dispose();
            }

            Assert.True(provider.ForceFlush());
        }

        // Other test classes run conversation turns in parallel while this listener is alive,
        // so the assertion is on the source of every span, and on this test's own span.
        Assert.All(exporter.Exported, e => Assert.Equal(ConversationTelemetry.SourceName, e.SourceName));
        Assert.Contains(exporter.Exported, e => e.DisplayName == spanName);

        // A processor wrapped around the SDK one would lose this: Langfuse reads the resource.
        Assert.Contains(exporter.Resource!.Attributes,
            a => a.Key == "service.name" && Equals(a.Value, TelemetryRegistration.ServiceName));
    }

    [Theory]
    [InlineData("Npgsql")]
    [InlineData("System.Net.Http")]
    public void TempoFilter_DropsClientSpansWithNoParent_AndKeepsThemUnderARoot(string clientSourceName)
    {
        var rootSourceName = $"Root-{Guid.NewGuid():N}";
        using var rootSource = new ActivitySource(rootSourceName);
        using var clientSource = new ActivitySource(clientSourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, rootSource) || ReferenceEquals(source, clientSource),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        // The test may itself run under an ambient activity.
        Activity.Current = null;

        // A poll loop or a backfill REST call: no request and no turn above it.
        using (var orphan = clientSource.StartActivity("heartbeat insert"))
            Assert.False(TelemetryRegistration.IsForTempo(orphan!));

        // A request or a conversation turn is a root and is kept, with what runs inside it.
        using (var root = rootSource.StartActivity("GET /api/x"))
        {
            Assert.True(TelemetryRegistration.IsForTempo(root!));
            using var child = clientSource.StartActivity("select");
            Assert.True(TelemetryRegistration.IsForTempo(child!));
        }
    }

    // The token of a webhook or an interaction is a path segment of the URL of a client span.
    [Theory]
    [InlineData("https://discord.com/api/webhooks/123/s3cret-Token_x", "https://discord.com/api/webhooks/123/REDACTED")]
    [InlineData("https://discord.com/api/v10/webhooks/123/s3cret/messages/@original?*", "https://discord.com/api/v10/webhooks/123/REDACTED/messages/@original?*")]
    [InlineData("https://discord.com/api/v10/interactions/456/s3cret/callback", "https://discord.com/api/v10/interactions/456/REDACTED/callback")]
    [InlineData("https://discord.com/api/v10/channels/1/messages/2", "https://discord.com/api/v10/channels/1/messages/2")]
    public void RedactUrlTokens_ReplacesTheTokenSegmentAndNothingElse(string url, string expected) =>
        Assert.Equal(expected, TelemetryRegistration.RedactUrlTokens(url));

    [Fact]
    public void RedactionProcessor_RewritesTheUrlTagOfASpanBeforeItIsExported()
    {
        var sourceName = $"Client-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        var exporter = new CapturingExporter();

        using (var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddProcessor(new UrlTokenRedactionProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build())
        {
            using var span = source.StartActivity("POST");
            span!.SetTag("url.full", "https://discord.com/api/webhooks/123/s3cret");
        }

        Assert.Equal("https://discord.com/api/webhooks/123/REDACTED", Assert.Single(exporter.Urls));
    }

    [Theory]
    [InlineData("/metrics", true)]
    [InlineData("/health", true)]
    [InlineData("/api/memes/search", false)]
    [InlineData("/", false)]
    public void IsProbe_NamesTheScrapeAndTheHealthCheck(string path, bool expected) =>
        Assert.Equal(expected, TelemetryRegistration.IsProbe(new PathString(path)));

    [Theory]
    [InlineData("http://tempo:4318", "http://tempo:4318/v1/traces")]
    [InlineData("http://tempo:4318/", "http://tempo:4318/v1/traces")]
    [InlineData("http://tempo:4318/v1/traces", "http://tempo:4318/v1/traces")]
    [InlineData(" http://tempo:4318/otlp ", "http://tempo:4318/otlp/v1/traces")]
    public void TracesUri_AppendsTheTracesPathOnce(string configured, string expected) =>
        Assert.Equal(expected, new TelemetryOptions { OtlpTracesEndpoint = configured }.TracesUri.ToString());

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://tempo:4318", true)]
    [InlineData("tempo:4318", false)]
    [InlineData("not a url", false)]
    public void HasValidTracesEndpoint_AcceptsEmptyOrAnAbsoluteHttpUrl(string? configured, bool expected) =>
        Assert.Equal(expected, new TelemetryOptions { OtlpTracesEndpoint = configured }.HasValidTracesEndpoint);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AddBotTelemetry_BuildsATracerProviderOnlyWhenATargetIsConfigured(bool langfuse, bool tempo)
    {
        var settings = new Dictionary<string, string?>();
        if (langfuse)
        {
            settings["Conversation:LangfuseHost"] = "http://127.0.0.1:1";
            settings["Conversation:LangfusePublicKey"] = "pk";
            settings["Conversation:LangfuseSecretKey"] = "sk";
        }

        if (tempo)
            settings["Telemetry:OtlpTracesEndpoint"] = "http://127.0.0.1:1";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBotTelemetry(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new TestHostEnvironment());
        using var provider = services.BuildServiceProvider();

        Assert.Equal(langfuse || tempo, provider.GetService<TracerProvider>() is not null);
    }

    // The contract the boot-smoke CI job greps for: the mapped endpoint answers with the
    // Prometheus text format, and carries the app meter next to the runtime one.
    [Fact]
    public async Task MetricsEndpoint_ExposesTheAppMeterAndTheRuntimeMeter()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddBotTelemetry(builder.Configuration, new TestHostEnvironment());

        await using var app = builder.Build();
        app.MapPrometheusScrapingEndpoint();
        app.MapFallback(() => Results.Content("<!doctype html>", "text/html"));
        await app.StartAsync();

        BotMetrics.EventHandled($"MetricsEndpointTest-{Guid.NewGuid():N}", BotMetrics.OutcomeOk, TimeSpan.FromMilliseconds(3));

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var response = await client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.DoesNotContain("<!doctype html>", body);
        Assert.Contains("wojtus_events_total{", body);
        Assert.Contains("wojtus_event_handler_duration_seconds_bucket{", body);
        Assert.Contains("wojtus_process_uptime_seconds", body);
        Assert.Contains("dotnet_gc_collections_total", body);
    }

    // Exemplars: a measurement made inside a sampled span carries its trace id, and only a
    // scrape that asks for OpenMetrics sees it. The plain text format must stay as it was:
    // the boot-smoke CI job and every older scraper read that one.
    [Fact]
    public async Task MetricsEndpoint_WritesTraceExemplarsToAnOpenMetricsScrapeOnly()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // A trace target makes the request span a sampled one. Nothing listens there.
            ["Telemetry:OtlpTracesEndpoint"] = "http://127.0.0.1:1",
        });
        builder.Services.AddBotTelemetry(builder.Configuration, new TestHostEnvironment());

        await using var app = builder.Build();
        app.MapPrometheusScrapingEndpoint();
        var phase = $"exemplar-{Guid.NewGuid():N}";
        app.MapGet("/api/traced", () =>
        {
            BotMetrics.MemeSearchPhaseFinished(phase, TimeSpan.FromMilliseconds(3));
            return Results.Ok(Activity.Current?.TraceId.ToString());
        });
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var traceId = (await client.GetStringAsync("/api/traced")).Trim('"');

        using var openMetricsRequest = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        openMetricsRequest.Headers.TryAddWithoutValidation("Accept", "application/openmetrics-text; version=1.0.0");
        using var openMetricsResponse = await client.SendAsync(openMetricsRequest);
        var openMetrics = await openMetricsResponse.Content.ReadAsStringAsync();
        var plain = await client.GetStringAsync("/metrics");

        Assert.Equal("application/openmetrics-text", openMetricsResponse.Content.Headers.ContentType!.MediaType);
        var ownBucket = openMetrics.Split('\n').First(line =>
            line.StartsWith("wojtus_meme_search_phase_duration_seconds_bucket{", StringComparison.Ordinal)
            && line.Contains($"phase=\"{phase}\"", StringComparison.Ordinal)
            && line.Contains(" # {", StringComparison.Ordinal));
        Assert.Contains($"# {{trace_id=\"{traceId}\",span_id=\"", ownBucket);

        Assert.DoesNotContain(" # {", plain);
        Assert.Contains($"phase=\"{phase}\"", plain);
    }

    private sealed class CapturingExporter : BaseExporter<Activity>
    {
        public List<(string SourceName, string DisplayName)> Exported { get; } = [];

        public List<object?> Urls { get; } = [];

        public Resource? Resource { get; private set; }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            Resource = ParentProvider.GetResource();
            foreach (var activity in batch)
            {
                Exported.Add((activity.Source.Name, activity.DisplayName));
                if (activity.GetTagItem("url.full") is { } url)
                    Urls.Add(url);
            }
            return ExportResult.Success;
        }
    }
}
