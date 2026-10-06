using System.Diagnostics.Tracing;

namespace DiscordEventService.Infrastructure;

// Makes a failed trace export visible. The OTLP exporter reports a collector it cannot reach
// only to its own EventSource, never to ILogger: with Tempo or Langfuse down, the application
// log and /metrics showed nothing.
//
// Every event of that source at Error or worse is counted (wojtus_trace_export_failures_total).
// The exporter retries each batch, so a dead collector gives an event every few seconds: one
// Warning per WarningInterval at most, with the number of failures since the last one.
//
// Registered as a hosted service only so that the host makes it at start and disposes it at
// stop; it does no work of its own.
internal sealed class TraceExportFailureListener : EventListener, IHostedService
{
    internal const string ExporterEventSourceName = "OpenTelemetry-Exporter-OpenTelemetryProtocol";

    internal static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly object _warningLock = new();

    // Nullable on purpose: the base constructor reports the event sources that already exist
    // before this constructor's body runs, so an event can arrive while the field is still null.
    private readonly ILogger<TraceExportFailureListener>? _logger;

    private DateTimeOffset? _lastWarningAt;
    private long _failuresSinceLastWarning;

    public TraceExportFailureListener(ILogger<TraceExportFailureListener> logger, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == ExporterEventSourceName)
            EnableEvents(eventSource, EventLevel.Error);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData) =>
        RecordFailure(eventData.EventName, eventData.Payload);

    // The payload holds the endpoint and the full exception text with its stack trace. Only
    // the first line of each part is kept: every line of a console log event is one more
    // entry in Loki.
    internal static string DescribePayload(IEnumerable<object?> payload) =>
        string.Join("; ", payload.Select(part => FirstLine(part?.ToString())));

    private static string? FirstLine(string? text) =>
        text?.IndexOf('\n') is >= 0 and var end ? text[..end].Trim() : text;

    // payload is the exporter's own text (the endpoint and the exception): it goes to the log,
    // never to a metric label, and it is read only when a warning is written.
    internal void RecordFailure(string? eventName, IEnumerable<object?>? payload)
    {
        BotMetrics.TraceExportFailed();
        if (_logger is null)
            return;

        long failures;
        lock (_warningLock)
        {
            _failuresSinceLastWarning++;
            var now = _time.GetUtcNow();
            if (_lastWarningAt is { } last && now - last < WarningInterval)
                return;

            _lastWarningAt = now;
            failures = _failuresSinceLastWarning;
            _failuresSinceLastWarning = 0;
        }

        _logger.LogWarning(
            "Trace export failed {FailureCount} time(s) since the last warning ({EventName}): {Detail}. Next warning in {IntervalMinutes} min at the earliest",
            failures, eventName ?? "unknown", payload is null ? "no detail" : DescribePayload(payload), (int)WarningInterval.TotalMinutes);
    }
}
