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
    private const int TimestampInvariantWindowHours = 24;
    private const int IntegrityAlertCooldownHours = 24;

    private static DateTime _lastFailedEventAlert = DateTime.MinValue;
    private static DateTime _lastIngestStallAlert = DateTime.MinValue;
    private static DateTime _lastEventRatioAlert = DateTime.MinValue;
    private static DateTime _lastCrashLoopAlert = DateTime.MinValue;
    private static DateTime _lastTimestampInvariantAlert = DateTime.MinValue;
    private static readonly Dictionary<string, int> _eventRatioDropStreaks = [];
    private static readonly Dictionary<string, DateTime> _alertedEpisodes = [];
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
        await CheckEventSilenceAsync(db, opts, now, cancellationToken);
        await CheckBackfillStallAsync(db, opts, now, cancellationToken);
        await CheckOpenDowntimeAsync(db, opts, now, cancellationToken);
        await CheckMessageTimestampInvariantAsync(db, opts, now, cancellationToken);
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
        if (!await IsGatewayFreshAsync(db, now, cancellationToken))
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
                && d.Type != Data.Entities.Core.BotDowntimeType.Deploy
                // #320: a DbUnreachable interval is an outage the process survived, not a restart.
                && d.Type != Data.Entities.Core.BotDowntimeType.DbUnreachable)
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
            .ToDictionaryAsync(g => g.EventType, g => g.Count, cancellationToken);

        var baselineTotals = await ComputeBaselineTotalsAsync(db, opts, recentStart, now, cancellationToken);
        var dropped = FindDroppedEventTypes(recent, baselineTotals, opts);
        var confirmedTypes = ConfirmDropStreaks(dropped.Select(d => d.EventType).ToHashSet(), opts);

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

    // Silence is checked per type against a fixed threshold, not a ratio: on this server only presence
    // has enough volume for a ratio, and the May blackout kept presence flowing while messages died.
    private async Task CheckEventSilenceAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var watched = opts.EventSilenceHours.Where(kvp => kvp.Value > 0).ToList();
        if (watched.Count == 0 || !await IsGatewayFreshAsync(db, now, cancellationToken))
            return;

        foreach (var (eventType, silenceHours) in watched)
        {
            var lastEvent = await db.RawEventLogs
                .Where(r => r.EventType == eventType)
                .MaxAsync(r => (DateTime?)r.ReceivedAtUtc, cancellationToken);

            // A type never seen has no silence to measure; the last event marks the episode.
            if (lastEvent is null || (now - lastEvent.Value).TotalHours < silenceHours)
                continue;

            var key = $"silence:{eventType}";
            if (!IsNewEpisode(key, lastEvent.Value))
                continue;

            var silentHours = (now - lastEvent.Value).TotalHours;
            if (!await SendWebhookAsync(opts.WebhookUrl!,
                $"**Event silence** — no `{eventType}` for {silentHours:F0}h (threshold {silenceHours}h) while the gateway is connected. Last one at {lastEvent.Value:yyyy-MM-dd HH:mm} UTC. Other event types may still be flowing — check handler errors and `raw_event_logs`.", cancellationToken))
                continue;

            RecordEpisode(key, lastEvent.Value);
            logger.LogWarning("Health check alert: no {EventType} events for {SilentHours:F0}h", eventType, silentHours);
        }
    }

    private async Task CheckBackfillStallAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var stallCutoff = now.AddHours(-opts.BackfillStallHours);
        var stalled = await db.BackfillCheckpoints
            .Where(c => c.Status == Data.Entities.Core.BackfillStatus.InProgress && c.LastUpdatedUtc < stallCutoff)
            .Select(c => new { c.Id, c.GuildDiscordId, c.Type, c.LastUpdatedUtc, c.ProcessedCount, c.TotalCount })
            .ToListAsync(cancellationToken);

        foreach (var checkpoint in stalled)
        {
            // Keyed on the last update, so a stuck row alerts once and a later stall of the same row alerts again.
            var key = $"backfill-stall:{checkpoint.Id}";
            if (!IsNewEpisode(key, checkpoint.LastUpdatedUtc))
                continue;

            var idleHours = (now - checkpoint.LastUpdatedUtc).TotalHours;
            if (!await SendWebhookAsync(opts.WebhookUrl!,
                $"**Backfill stalled** — `{checkpoint.Type}` for guild {checkpoint.GuildDiscordId} is InProgress with no progress for {idleHours:F0}h ({checkpoint.ProcessedCount}/{checkpoint.TotalCount?.ToString() ?? "?"}). If Hangfire shows the job Processing, it hung; otherwise the process died mid-run and the next chain resumes it.", cancellationToken))
                continue;

            RecordEpisode(key, checkpoint.LastUpdatedUtc);
            logger.LogWarning("Health check alert: {BackfillType} backfill for guild {GuildId} stalled for {IdleHours:F0}h",
                checkpoint.Type, checkpoint.GuildDiscordId, idleHours);
        }
    }

    private async Task CheckOpenDowntimeAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        var openCutoff = now.AddMinutes(-opts.OpenDowntimeMaxMinutes);
        var open = await db.BotDowntimeIntervals
            .Where(d => d.EndedAtUtc == null && d.StartedAtUtc < openCutoff)
            .OrderBy(d => d.StartedAtUtc)
            .Select(d => new { d.Id, d.Type, d.StartedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (open is null)
            return;

        // While the gateway is down an open row is the correct state, not a leak.
        var key = $"open-downtime:{open.Id}";
        if (!IsNewEpisode(key, open.StartedAtUtc) || !await IsGatewayFreshAsync(db, now, cancellationToken))
            return;

        var openMinutes = (now - open.StartedAtUtc).TotalMinutes;
        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Downtime row left open** — `{open.Type}` row {open.Id} opened {openMinutes:F0} min ago and the gateway is connected. New downtime rows cannot open until it closes.", cancellationToken))
            return;

        RecordEpisode(key, open.StartedAtUtc);
        logger.LogWarning("Health check alert: downtime row {DowntimeId} of type {DowntimeType} open for {OpenMinutes:F0} min with the gateway connected",
            open.Id, open.Type, openMinutes);
    }

    // Discord timestamps every MESSAGE_CREATE, so equality with the receive clock means a handler fell
    // back to its own clock (#59).
    private async Task CheckMessageTimestampInvariantAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if ((now - _lastTimestampInvariantAlert).TotalHours < IntegrityAlertCooldownHours)
                return;
        }

        var windowStart = now.AddHours(-TimestampInvariantWindowHours);
        var count = await db.MessageEvents
            .Where(m => m.EventType == Data.Entities.Events.MessageEventType.Created
                && m.ReceivedAtUtc > windowStart
                && m.EventTimestampUtc == m.ReceivedAtUtc)
            .CountAsync(cancellationToken);

        if (count == 0)
            return;

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Data integrity** — {count} `MessageCreated` row(s) in the last {TimestampInvariantWindowHours}h have `event_timestamp_utc = received_at_utc`. A handler is using its own clock instead of Discord's timestamp.", cancellationToken))
            return;

        lock (_lock) { _lastTimestampInvariantAlert = now; }

        logger.LogWarning("Health check alert: {Count} MessageCreated events carry the receive time as their event time", count);
    }

    private static async Task<bool> IsGatewayFreshAsync(DiscordDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var lastConnectedHeartbeat = await db.BotHeartbeats
            .Where(h => h.IsGatewayConnected == true)
            .OrderByDescending(h => h.LastHeartbeatUtc)
            .Select(h => (DateTime?)h.LastHeartbeatUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return lastConnectedHeartbeat is { } heartbeat && (now - heartbeat).TotalSeconds <= HeartbeatFreshSeconds;
    }

    // Episode alerts fire once per distinct marker, so a condition that lasts for days alerts once
    // instead of on every cooldown window — the repetition that made the old night alerts noisy.
    private static bool IsNewEpisode(string key, DateTime marker)
    {
        lock (_lock)
            return !(_alertedEpisodes.TryGetValue(key, out var alerted) && alerted == marker);
    }

    private static void RecordEpisode(string key, DateTime marker)
    {
        lock (_lock) { _alertedEpisodes[key] = marker; }
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

    // Baseline = the same wall-clock window on each of the previous N days, so
    // Discord's day/night activity cycle doesn't read as a drop (voice events
    // legitimately fall to 0 overnight). Whole-day offsets keep this comparison
    // independent of the database session timezone.
    private static async Task<Dictionary<string, int>> ComputeBaselineTotalsAsync(
        DiscordDbContext db, HealthCheckOptions opts, DateTime recentStart, DateTime now, CancellationToken cancellationToken)
    {
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

        return baselineTotals;
    }

    private static List<(string EventType, double ExpectedInWindow)> FindDroppedEventTypes(
        Dictionary<string, int> recent, Dictionary<string, int> baselineTotals, HealthCheckOptions opts)
    {
        var excluded = new HashSet<string>(opts.EventRatioExcludedEventTypes, StringComparer.OrdinalIgnoreCase);

        return baselineTotals
            .Where(b => !excluded.Contains(b.Key))
            .Select(b => (EventType: b.Key, ExpectedInWindow: (double)b.Value / opts.EventRatioBaselineDays))
            .Where(b => b.ExpectedInWindow >= opts.EventRatioMinWindowBaseline)
            .Where(b => recent.GetValueOrDefault(b.EventType, 0) < b.ExpectedInWindow * opts.EventRatioDropThreshold)
            .ToList();
    }

    // A drop must persist across EventRatioConsecutiveRuns consecutive runs before it
    // alerts; types that recover have their streak cleared. Only "confirmed" types are
    // eligible — debounces transient lulls on a low-traffic server.
    private static List<string> ConfirmDropStreaks(HashSet<string> droppedTypes, HealthCheckOptions opts)
    {
        lock (_lock)
        {
            foreach (var key in _eventRatioDropStreaks.Keys.Where(k => !droppedTypes.Contains(k)).ToList())
                _eventRatioDropStreaks.Remove(key);

            foreach (var type in droppedTypes)
                _eventRatioDropStreaks[type] = _eventRatioDropStreaks.GetValueOrDefault(type) + 1;

            return _eventRatioDropStreaks
                .Where(kvp => kvp.Value >= opts.EventRatioConsecutiveRuns)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }
}
