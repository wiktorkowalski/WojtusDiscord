using System.Diagnostics.Metrics;
using Hangfire.Storage.Monitoring;

namespace DiscordEventService.Infrastructure;

// The bot's own metrics, exposed on /metrics by the Prometheus exporter (Program.cs).
//
// Static on purpose, like ConversationTelemetry: event handlers run in the DSharpPlus child
// container, and a static Meter needs no IMeterFactory forwarded into it.
//
// Rules for anything added here:
// - Labels are low cardinality only: event type, handler, outcome, model, tool, job type, level.
//   Never an id (user, channel, guild, message), a query text or an exception message.
// - A helper does no I/O and does not throw: it is called on the event hot path.
// - Durations are in seconds and sizes in bytes. The exporter appends the unit and, for a
//   counter, "_total": "wojtus.events" is scraped as "wojtus_events_total".
internal static class BotMetrics
{
    public const string MeterName = "DiscordEventService";

    public const string OutcomeOk = "ok";
    public const string OutcomeFailed = "failed";

    private static readonly Meter Meter = new(MeterName);

    // The SDK default boundaries are for milliseconds: every value in seconds would fall into
    // the first bucket. A static Meter cannot take an SDK view, so the boundaries ride on the
    // instrument as advice.
    private static readonly double[] FastSeconds = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];
    private static readonly double[] ModelSeconds = [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20, 30, 60, 120, 300];
    private static readonly double[] JobSeconds = [1, 5, 15, 30, 60, 300, 900, 1800, 3600, 7200, 14400];
    private static readonly double[] SizeBytes = [256, 1024, 4096, 16384, 65536, 262144, 1048576];
    private static readonly double[] ResultCounts = [0, 1, 2, 5, 10, 25, 50, 100];

    // Gateway events (EventPipeline).
    private static readonly Counter<long> Events = Meter.CreateCounter<long>(
        "wojtus.events", description: "Gateway events through the event pipeline, by outcome.");
    private static readonly Histogram<double> EventHandlerDuration = Seconds(
        "wojtus.event.handler.duration", "Time one gateway event spends in the event pipeline.", FastSeconds);
    private static readonly Histogram<double> RawEventSize = Meter.CreateHistogram(
        "wojtus.event.raw.size", unit: "By", description: "Size of the JSON stored in raw_event_logs.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = SizeBytes });

    // Failures.
    private static readonly Counter<long> EventFailures = Meter.CreateCounter<long>(
        "wojtus.event.failures", description: "Failures handed to FailedEventService (hard and soft).");
    private static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>(
        "wojtus.event.dead_letters", description: "Failures the database refused, sent to the JSONL fallback.");
    private static readonly Counter<long> UnwritableFailedWrites = Meter.CreateCounter<long>(
        "wojtus.db.unwritable.failed_writes", description: "Failed heartbeat writes counted toward an unwritable window.");
    private static readonly Counter<long> DowntimeIntervals = Meter.CreateCounter<long>(
        "wojtus.downtime.intervals", description: "Downtime rows written, by type.");

    // Gateway state.
    private static readonly Counter<long> HeartbeatWrites = Meter.CreateCounter<long>(
        "wojtus.heartbeat.writes", description: "Heartbeat rows written to the database, by outcome.");
    private static readonly Counter<long> SocketsClosed = Meter.CreateCounter<long>(
        "wojtus.gateway.socket.closed", description: "Gateway socket closes, by close code.");
    private static readonly Counter<long> SessionsResumed = Meter.CreateCounter<long>(
        "wojtus.gateway.session.resumed", description: "Gateway sessions resumed (warm reconnect).");
    private static readonly Counter<long> GuildDownloads = Meter.CreateCounter<long>(
        "wojtus.gateway.guild_download.completed", description: "Guild downloads completed (cold connect).");

    // Conversation.
    private static readonly Counter<long> ConversationTurns = Meter.CreateCounter<long>(
        "wojtus.conversation.turns", description: "Conversation turns, by outcome.");
    private static readonly Histogram<double> ConversationTurnDuration = Seconds(
        "wojtus.conversation.turn.duration", "Time from the start of a conversation turn to its last message.", ModelSeconds);
    private static readonly Counter<long> ConversationRounds = Meter.CreateCounter<long>(
        "wojtus.conversation.rounds", description: "Model calls of the conversation loop (one per usage ledger row).");
    private static readonly Counter<long> ConversationRetries = Meter.CreateCounter<long>(
        "wojtus.conversation.retries", description: "Model calls that were a second or later attempt of a round.");
    private static readonly Counter<long> ConversationTokens = Meter.CreateCounter<long>(
        "wojtus.conversation.tokens", description: "Tokens billed by conversation model calls, by direction.");
    private static readonly Counter<double> ConversationCost = Meter.CreateCounter<double>(
        "wojtus.conversation.cost.usd", description: "Cost of conversation model calls in USD.");
    private static readonly Histogram<double> ConversationRoundDuration = Seconds(
        "wojtus.conversation.round.duration", "Latency of one conversation model call.", ModelSeconds);
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "wojtus.conversation.tool.calls", description: "Conversation tool calls, by tool and outcome.");
    private static readonly Histogram<double> ToolDuration = Seconds(
        "wojtus.conversation.tool.duration", "Time one conversation tool call takes.", FastSeconds);
    private static readonly Counter<long> UsageAlerts = Meter.CreateCounter<long>(
        "wojtus.conversation.usage_alerts", description: "Cost-cap alerts fired, by cap.");

    // Meme search and indexing.
    private static readonly Counter<long> MemeSearches = Meter.CreateCounter<long>(
        "wojtus.meme.searches", description: "Meme searches, by caller kind.");
    private static readonly Histogram<double> MemeSearchDuration = Seconds(
        "wojtus.meme.search.duration", "Time one meme search takes.", FastSeconds);
    private static readonly Histogram<double> MemeSearchResults = Meter.CreateHistogram(
        "wojtus.meme.search.results", description: "Hits one meme search page returns.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = ResultCounts });
    private static readonly Counter<long> MemeIndexOutcomes = Meter.CreateCounter<long>(
        "wojtus.meme.index.outcomes", description: "Meme indexing outcomes per attachment.");
    private static readonly Counter<long> MemeVisionCalls = Meter.CreateCounter<long>(
        "wojtus.meme.vision.calls", description: "Vision model calls for meme analysis, by model and outcome.");
    private static readonly Histogram<double> MemeVisionDuration = Seconds(
        "wojtus.meme.vision.duration", "Latency of one vision model call.", ModelSeconds);
    private static readonly Counter<long> MemeVisionTokens = Meter.CreateCounter<long>(
        "wojtus.meme.vision.tokens", description: "Tokens billed by vision model calls, by direction.");
    private static readonly Counter<double> MemeVisionCost = Meter.CreateCounter<double>(
        "wojtus.meme.vision.cost.usd", description: "Cost of vision model calls in USD.");
    private static readonly Counter<long> MemeImportItems = Meter.CreateCounter<long>(
        "wojtus.meme.import.items", description: "Meme annotation import items, by outcome.");

    // Backfill and jobs.
    private static readonly Counter<long> BackfillRuns = Meter.CreateCounter<long>(
        "wojtus.backfill.runs", description: "Backfill job runs, by type and outcome.");
    private static readonly Histogram<double> BackfillRunDuration = Seconds(
        "wojtus.backfill.run.duration", "Time one backfill job run takes.", JobSeconds);
    private static readonly Counter<long> BackfillItems = Meter.CreateCounter<long>(
        "wojtus.backfill.items", description: "Items (channels) a cursor backfill finished.");
    private static readonly Counter<long> BackfillItemErrors = Meter.CreateCounter<long>(
        "wojtus.backfill.item.errors", description: "Items (channels) a cursor backfill failed on and skipped.");
    private static readonly Counter<long> HealthCheckAlerts = Meter.CreateCounter<long>(
        "wojtus.healthcheck.alerts", description: "Health check alerts sent, by check.");
    private static readonly Counter<long> HealthCheckWebhookFailures = Meter.CreateCounter<long>(
        "wojtus.healthcheck.webhook.failures", description: "Health check webhook sends that failed.");

    // Log events (LogEventCounterProvider).
    private static readonly Counter<long> LogEvents = Meter.CreateCounter<long>(
        "wojtus.log.events", description: "Log events that passed the log level filter, by level.");

    // One boxed tag per level: the log path must not allocate.
    private static readonly KeyValuePair<string, object?>[] LogLevelTags =
        [.. Enum.GetValues<LogLevel>().Select(level => Tag("level", level.ToString().ToLowerInvariant()))];

    private static Func<(bool? Connected, int? LatencyMs)>? _gatewayReader;
    private static Func<bool>? _unwritableWindowReader;
    private static string? _buildCommit;
    private static volatile StatisticsDto? _hangfireStatistics;

    // The observable instruments read state someone else owns. Each callback returns nothing
    // until its source is set, and nothing when the source throws: a scrape must not fail
    // over one gauge.
    static BotMetrics()
    {
        Meter.CreateObservableGauge("wojtus.gateway.connected", ObserveGatewayConnected,
            description: "1 when every gateway shard is connected, else 0.");
        Meter.CreateObservableGauge("wojtus.gateway.latency", ObserveGatewayLatency, unit: "s",
            description: "Gateway heartbeat latency.");
        Meter.CreateObservableGauge("wojtus.db.unwritable.window.pending", () => _unwritableWindowReader?.Invoke() == true ? 1 : 0,
            description: "1 while an unwritable-database window is open or waits for its row.");
        Meter.CreateObservableGauge("wojtus.process.start.time",
            () => (BootClock.StartedAtUtc - DateTime.UnixEpoch).TotalSeconds, unit: "s",
            description: "Boot instant of this process as a Unix time.");
        Meter.CreateObservableGauge("wojtus.process.uptime",
            () => (DateTime.UtcNow - BootClock.StartedAtUtc).TotalSeconds, unit: "s",
            description: "Time since boot.");
        Meter.CreateObservableGauge("wojtus.build.info", ObserveBuildInfo,
            description: "Always 1; the commit label is the deployed build.");
        Meter.CreateObservableGauge("wojtus.hangfire.jobs", ObserveHangfireJobs,
            description: "Hangfire jobs, by state.");
        Meter.CreateObservableGauge("wojtus.hangfire.servers", () => ObserveHangfire(statistics => statistics.Servers),
            description: "Hangfire servers alive.");
        Meter.CreateObservableGauge("wojtus.hangfire.recurring.jobs", () => ObserveHangfire(statistics => statistics.Recurring),
            description: "Hangfire recurring jobs registered.");
    }

    public static void EventHandled(string eventType, string outcome, TimeSpan elapsed)
    {
        var eventTypeTag = Tag("event_type", eventType);
        var outcomeTag = Tag("outcome", outcome);
        Events.Add(1, eventTypeTag, outcomeTag);
        EventHandlerDuration.Record(elapsed.TotalSeconds, eventTypeTag, outcomeTag);
    }

    public static void RawEventLogged(string eventType, int sizeBytes) =>
        RawEventSize.Record(sizeBytes, Tag("event_type", eventType));

    public static void EventFailureRecorded(string eventType, string handlerName) =>
        EventFailures.Add(1, Tag("event_type", eventType), Tag("handler", handlerName));

    public static void DeadLetterWritten(string eventType, string handlerName) =>
        DeadLetters.Add(1, Tag("event_type", eventType), Tag("handler", handlerName));

    public static void UnwritableWriteFailed() => UnwritableFailedWrites.Add(1);

    public static void DowntimeIntervalWritten(string type) => DowntimeIntervals.Add(1, Tag("type", type));

    public static void HeartbeatWritten(bool succeeded) =>
        HeartbeatWrites.Add(1, Tag("outcome", succeeded ? OutcomeOk : OutcomeFailed));

    public static void SocketClosed(int closeCode) => SocketsClosed.Add(1, Tag("close_code", closeCode));

    public static void SessionResumed() => SessionsResumed.Add(1);

    public static void GuildDownloadCompleted() => GuildDownloads.Add(1);

    public static void ConversationTurnFinished(string outcome, TimeSpan elapsed)
    {
        var outcomeTag = Tag("outcome", outcome);
        ConversationTurns.Add(1, outcomeTag);
        ConversationTurnDuration.Record(elapsed.TotalSeconds, outcomeTag);
    }

    public static void ConversationRoundRecorded(
        string model, int attempt, int? promptTokens, int? completionTokens, double? costUsd, long latencyMs, bool failed)
    {
        var modelTag = Tag("model", model);
        var outcomeTag = Tag("outcome", failed ? OutcomeFailed : OutcomeOk);

        ConversationRounds.Add(1, modelTag, outcomeTag);
        ConversationRoundDuration.Record(latencyMs / 1000d, modelTag, outcomeTag);
        if (attempt > 1)
            ConversationRetries.Add(1, modelTag);
        if (promptTokens is > 0)
            ConversationTokens.Add(promptTokens.Value, modelTag, Tag("direction", "input"));
        if (completionTokens is > 0)
            ConversationTokens.Add(completionTokens.Value, modelTag, Tag("direction", "output"));
        if (costUsd is > 0)
            ConversationCost.Add(costUsd.Value, modelTag);
    }

    public static void ToolCalled(string tool, string outcome, TimeSpan elapsed)
    {
        var toolTag = Tag("tool", tool);
        var outcomeTag = Tag("outcome", outcome);
        ToolCalls.Add(1, toolTag, outcomeTag);
        ToolDuration.Record(elapsed.TotalSeconds, toolTag, outcomeTag);
    }

    public static void UsageAlertFired(string cap) => UsageAlerts.Add(1, Tag("cap", cap));

    public static void MemeSearched(string caller, int hitCount, TimeSpan elapsed)
    {
        var callerTag = Tag("caller", caller);
        MemeSearches.Add(1, callerTag);
        MemeSearchDuration.Record(elapsed.TotalSeconds, callerTag);
        MemeSearchResults.Record(hitCount, callerTag);
    }

    public static void MemeIndexOutcome(string outcome) => MemeIndexOutcomes.Add(1, Tag("outcome", outcome));

    public static void MemeVisionCalled(
        string model, string outcome, TimeSpan elapsed, int promptTokens, int completionTokens, decimal? costUsd)
    {
        var modelTag = Tag("model", model);
        var outcomeTag = Tag("outcome", outcome);
        MemeVisionCalls.Add(1, modelTag, outcomeTag);
        MemeVisionDuration.Record(elapsed.TotalSeconds, modelTag, outcomeTag);
        if (promptTokens > 0)
            MemeVisionTokens.Add(promptTokens, modelTag, Tag("direction", "input"));
        if (completionTokens > 0)
            MemeVisionTokens.Add(completionTokens, modelTag, Tag("direction", "output"));
        if (costUsd is > 0)
            MemeVisionCost.Add((double)costUsd.Value, modelTag);
    }

    public static void MemeImportFinished(int imported, int skipped, int rejected)
    {
        if (imported > 0)
            MemeImportItems.Add(imported, Tag("outcome", "imported"));
        if (skipped > 0)
            MemeImportItems.Add(skipped, Tag("outcome", "skipped"));
        if (rejected > 0)
            MemeImportItems.Add(rejected, Tag("outcome", "rejected"));
    }

    public static void BackfillRunFinished(string type, string outcome, TimeSpan elapsed)
    {
        var typeTag = Tag("type", type);
        var outcomeTag = Tag("outcome", outcome);
        BackfillRuns.Add(1, typeTag, outcomeTag);
        BackfillRunDuration.Record(elapsed.TotalSeconds, typeTag, outcomeTag);
    }

    public static void BackfillItemFinished(string type, bool failed) =>
        (failed ? BackfillItemErrors : BackfillItems).Add(1, Tag("type", type));

    public static void HealthCheckAlertSent(string check) => HealthCheckAlerts.Add(1, Tag("check", check));

    public static void HealthCheckWebhookFailed() => HealthCheckWebhookFailures.Add(1);

    public static void LogEventWritten(LogLevel level)
    {
        var index = (int)level;
        if ((uint)index < (uint)LogLevelTags.Length)
            LogEvents.Add(1, LogLevelTags[index]);
    }

    // The sources of the observable gauges, each set once by its owner: Program.cs (build,
    // gateway), HeartbeatBackgroundService (window) and HangfireStatisticsService.
    public static void SetBuildInfo(BuildInfo build) => _buildCommit = build.CommitShort;

    public static void SetGatewayStateReader(Func<(bool? Connected, int? LatencyMs)> reader) => _gatewayReader = reader;

    public static void SetUnwritableWindowReader(Func<bool> reader) => _unwritableWindowReader = reader;

    // null = the last read failed: the gauges are then absent, not stale.
    public static void SetHangfireStatistics(StatisticsDto? statistics) => _hangfireStatistics = statistics;

    private static IEnumerable<Measurement<int>> ObserveGatewayConnected()
    {
        if (ReadGateway().Connected is { } connected)
            yield return new Measurement<int>(connected ? 1 : 0);
    }

    private static IEnumerable<Measurement<double>> ObserveGatewayLatency()
    {
        if (ReadGateway().LatencyMs is { } latencyMs)
            yield return new Measurement<double>(latencyMs / 1000d);
    }

    private static (bool? Connected, int? LatencyMs) ReadGateway()
    {
        try
        {
            return _gatewayReader?.Invoke() ?? default;
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static IEnumerable<Measurement<int>> ObserveBuildInfo()
    {
        if (_buildCommit is { } commit)
            yield return new Measurement<int>(1, Tag("commit", commit));
    }

    private static IEnumerable<Measurement<long>> ObserveHangfireJobs()
    {
        if (_hangfireStatistics is not { } statistics)
            yield break;

        yield return new Measurement<long>(statistics.Enqueued, Tag("state", "enqueued"));
        yield return new Measurement<long>(statistics.Scheduled, Tag("state", "scheduled"));
        yield return new Measurement<long>(statistics.Processing, Tag("state", "processing"));
        yield return new Measurement<long>(statistics.Failed, Tag("state", "failed"));
        yield return new Measurement<long>(statistics.Succeeded, Tag("state", "succeeded"));
    }

    private static IEnumerable<Measurement<long>> ObserveHangfire(Func<StatisticsDto, long> select)
    {
        if (_hangfireStatistics is { } statistics)
            yield return new Measurement<long>(select(statistics));
    }

    private static Histogram<double> Seconds(string name, string description, double[] boundaries) =>
        Meter.CreateHistogram(name, unit: "s", description: description,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = boundaries });

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
