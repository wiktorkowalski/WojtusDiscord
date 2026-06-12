using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Jobs;

internal sealed class HealthCheckJob(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<HealthCheckOptions> options,
    ILogger<HealthCheckJob> logger)
{
    // Inline tuning knobs; the operator-facing thresholds live in HealthCheckOptions.
    private const int RecentFailureDisplayCount = 5;
    private const int HeartbeatFreshSeconds = 30;
    private const int CrashLoopWindowMinutes = 30;
    private const int CrashLoopRestartThreshold = 3;

    private static DateTime _lastFailedEventAlert = DateTime.MinValue;
    private static DateTime _lastIngestStallAlert = DateTime.MinValue;
    private static DateTime _lastEventRatioAlert = DateTime.MinValue;
    private static DateTime _lastCrashLoopAlert = DateTime.MinValue;
    private static readonly Dictionary<string, int> _eventRatioDropStreaks = [];
    private static readonly object _lock = new object();
    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(10);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.WebhookUrl))
        {
            logger.LogDebug("Health check skipped: no webhook URL configured");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var now = DateTime.UtcNow;

        await CheckFailedEventsAsync(db, opts, now, cancellationToken);
        await CheckIngestStallAsync(db, opts, now, cancellationToken);
        await CheckEventTypeRatioAsync(db, opts, now, cancellationToken);
        await CheckCrashLoopAsync(db, opts, now, cancellationToken);
    }

    private async Task CheckFailedEventsAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var windowStart = now.AddMinutes(-opts.FailedEventWindowMinutes);
        var count = await db.FailedEvents
            .Where(f => f.FailedAtUtc > windowStart && !f.IsResolved)
            .CountAsync(cancellationToken);

        if (count == 0)
            return;

        lock (_lock)
        {
            if ((now - _lastFailedEventAlert).TotalMinutes < opts.AlertCooldownMinutes)
                return;
        }

        var recent = await db.FailedEvents
            .Where(f => f.FailedAtUtc > windowStart && !f.IsResolved)
            .OrderByDescending(f => f.FailedAtUtc)
            .Take(RecentFailureDisplayCount)
            .Select(f => new { f.EventType, f.HandlerName, f.ExceptionType, f.FailedAtUtc })
            .ToListAsync(cancellationToken);

        var details = string.Join("\n", recent.Select(r =>
            $"- `{r.EventType}` in `{r.HandlerName}` ({r.ExceptionType}) at {r.FailedAtUtc:HH:mm:ss}"));

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**{count} failed event(s)** in the last {opts.FailedEventWindowMinutes} min\n{details}", cancellationToken))
            return;

        lock (_lock) { _lastFailedEventAlert = now; }

        logger.LogWarning("Health check alert: {Count} failed events in last {Window} min", count, opts.FailedEventWindowMinutes);
    }

    private async Task CheckIngestStallAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var lastConnectedHeartbeat = await db.BotHeartbeats
            .Where(h => h.IsGatewayConnected == true)
            .OrderByDescending(h => h.LastHeartbeatUtc)
            .Select(h => (DateTime?)h.LastHeartbeatUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastConnectedHeartbeat is null)
            return;

        var heartbeatAge = now - lastConnectedHeartbeat.Value;
        if (heartbeatAge.TotalSeconds > HeartbeatFreshSeconds)
            return;

        var lastEvent = await db.RawEventLogs
            .OrderByDescending(r => r.ReceivedAtUtc)
            .Select(r => (DateTime?)r.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastEvent is null)
            return;

        var eventAge = now - lastEvent.Value;
        if (eventAge.TotalMinutes < opts.IngestStallMinutes)
            return;

        lock (_lock)
        {
            if ((now - _lastIngestStallAlert).TotalMinutes < opts.AlertCooldownMinutes)
                return;
        }

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Ingest stall detected** — bot is connected but no events for {eventAge.TotalMinutes:F0} min (last event at {lastEvent.Value:yyyy-MM-dd HH:mm:ss} UTC)", cancellationToken))
            return;

        lock (_lock) { _lastIngestStallAlert = now; }

        logger.LogWarning("Health check alert: ingest stall, last event {MinutesAgo:F0} min ago", eventAge.TotalMinutes);
    }

    private async Task CheckCrashLoopAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var windowStart = now.AddMinutes(-CrashLoopWindowMinutes);
        var recentRestarts = await db.BotDowntimeIntervals
            .Where(d => d.StartedAtUtc > windowStart && d.EndedAtUtc != null
                && d.Type != Data.Entities.Core.BotDowntimeType.GracefulShutdown
                && d.Type != Data.Entities.Core.BotDowntimeType.Deploy)
            .CountAsync(cancellationToken);

        if (recentRestarts < CrashLoopRestartThreshold)
            return;

        lock (_lock)
        {
            if ((now - _lastCrashLoopAlert).TotalMinutes < opts.AlertCooldownMinutes)
                return;
        }

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Possible crash-loop** — {recentRestarts} restarts in the last {CrashLoopWindowMinutes} minutes. Check container logs for `Stack overflow` or other fatal errors.", cancellationToken))
            return;

        lock (_lock) { _lastCrashLoopAlert = now; }

        logger.LogWarning("Health check alert: {Restarts} restarts in last {WindowMinutes} min — possible crash-loop", recentRestarts, CrashLoopWindowMinutes);
    }

    private async Task CheckEventTypeRatioAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var recentStart = now.AddHours(-opts.EventRatioRecentHours);

        var recent = await db.RawEventLogs
            .Where(r => r.ReceivedAtUtc > recentStart)
            .GroupBy(r => r.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EventType, g => g.Count);

        // Baseline = the same wall-clock window on each of the previous N days, so
        // Discord's day/night activity cycle doesn't read as a drop (voice events
        // legitimately fall to 0 overnight). Whole-day offsets keep this comparison
        // independent of the database session timezone.
        var baselineTotals = new Dictionary<string, int>();
        for (var dayOffset = 1; dayOffset <= opts.EventRatioBaselineDays; dayOffset++)
        {
            var windowStart = recentStart.AddDays(-dayOffset);
            var windowEnd = now.AddDays(-dayOffset);
            var counts = await db.RawEventLogs
                .Where(r => r.ReceivedAtUtc > windowStart && r.ReceivedAtUtc <= windowEnd)
                .GroupBy(r => r.EventType)
                .Select(g => new { EventType = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            foreach (var c in counts)
                baselineTotals[c.EventType] = baselineTotals.GetValueOrDefault(c.EventType) + c.Count;
        }

        var excluded = new HashSet<string>(opts.EventRatioExcludedEventTypes, StringComparer.OrdinalIgnoreCase);

        var dropped = baselineTotals
            .Where(b => !excluded.Contains(b.Key))
            .Select(b => new { EventType = b.Key, ExpectedInWindow = (double)b.Value / opts.EventRatioBaselineDays })
            .Where(b => b.ExpectedInWindow >= opts.EventRatioMinWindowBaseline)
            .Where(b => recent.GetValueOrDefault(b.EventType, 0) < b.ExpectedInWindow * opts.EventRatioDropThreshold)
            .ToList();

        // A drop must persist across EventRatioConsecutiveRuns consecutive runs before it
        // alerts; types that recover have their streak cleared. Only "confirmed" types are
        // eligible — debounces transient lulls on a low-traffic server.
        var droppedTypes = dropped.Select(d => d.EventType).ToHashSet();
        List<string> confirmedTypes;
        lock (_lock)
        {
            foreach (var key in _eventRatioDropStreaks.Keys.Where(k => !droppedTypes.Contains(k)).ToList())
                _eventRatioDropStreaks.Remove(key);

            foreach (var type in droppedTypes)
                _eventRatioDropStreaks[type] = _eventRatioDropStreaks.GetValueOrDefault(type) + 1;

            confirmedTypes = _eventRatioDropStreaks
                .Where(kvp => kvp.Value >= opts.EventRatioConsecutiveRuns)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        var confirmed = dropped.Where(d => confirmedTypes.Contains(d.EventType)).ToList();
        if (confirmed.Count == 0)
            return;

        lock (_lock)
        {
            if ((now - _lastEventRatioAlert).TotalMinutes < opts.AlertCooldownMinutes)
                return;
        }

        var details = string.Join("\n", confirmed.Select(d =>
        {
            var recentCount = recent.GetValueOrDefault(d.EventType, 0);
            return $"- `{d.EventType}`: {recentCount} in last {opts.EventRatioRecentHours}h (expected ~{d.ExpectedInWindow:F1} for this time of day)";
        }));

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Event type ratio drop** — {confirmed.Count} event type(s) below {opts.EventRatioDropThreshold:P0} of baseline for {opts.EventRatioConsecutiveRuns}+ runs:\n{details}", cancellationToken))
            return;

        lock (_lock) { _lastEventRatioAlert = now; }

        logger.LogWarning("Health check alert: event type ratio drop for {Types}",
            string.Join(", ", confirmed.Select(d => d.EventType)));
    }

    private async Task<bool> SendWebhookAsync(string webhookUrl, string message, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = WebhookTimeout;
            var payload = JsonSerializer.Serialize(new { content = message });
            using var response = await client.PostAsync(webhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Webhook POST failed: {StatusCode} {Body}",
                    response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send health check webhook");
            return false;
        }
    }
}
