using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Jobs;

public class HealthCheckJob(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<HealthCheckOptions> options,
    ILogger<HealthCheckJob> logger)
{
    private static DateTime _lastFailedEventAlert = DateTime.MinValue;
    private static DateTime _lastIngestStallAlert = DateTime.MinValue;
    private static DateTime _lastEventRatioAlert = DateTime.MinValue;
    private static DateTime _lastCrashLoopAlert = DateTime.MinValue;
    private static readonly object _lock = new();
    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(10);

    public async Task ExecuteAsync()
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

        await CheckFailedEventsAsync(db, opts, now);
        await CheckIngestStallAsync(db, opts, now);
        await CheckEventTypeRatioAsync(db, opts, now);
        await CheckCrashLoopAsync(db, opts, now);
    }

    private async Task CheckFailedEventsAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now)
    {
        var windowStart = now.AddMinutes(-opts.FailedEventWindowMinutes);
        var count = await db.FailedEvents
            .Where(f => f.FailedAtUtc > windowStart && !f.IsResolved)
            .CountAsync();

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
            .Take(5)
            .Select(f => new { f.EventType, f.HandlerName, f.ExceptionType, f.FailedAtUtc })
            .ToListAsync();

        var details = string.Join("\n", recent.Select(r =>
            $"- `{r.EventType}` in `{r.HandlerName}` ({r.ExceptionType}) at {r.FailedAtUtc:HH:mm:ss}"));

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**{count} failed event(s)** in the last {opts.FailedEventWindowMinutes} min\n{details}"))
            return;

        lock (_lock) { _lastFailedEventAlert = now; }

        logger.LogWarning("Health check alert: {Count} failed events in last {Window} min", count, opts.FailedEventWindowMinutes);
    }

    private async Task CheckIngestStallAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now)
    {
        var lastConnectedHeartbeat = await db.BotHeartbeats
            .Where(h => h.IsGatewayConnected == true)
            .OrderByDescending(h => h.LastHeartbeatUtc)
            .Select(h => (DateTime?)h.LastHeartbeatUtc)
            .FirstOrDefaultAsync();

        if (lastConnectedHeartbeat is null)
            return;

        var heartbeatAge = now - lastConnectedHeartbeat.Value;
        if (heartbeatAge.TotalSeconds > 30)
            return;

        var lastEvent = await db.RawEventLogs
            .OrderByDescending(r => r.ReceivedAtUtc)
            .Select(r => (DateTime?)r.ReceivedAtUtc)
            .FirstOrDefaultAsync();

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
            $"**Ingest stall detected** — bot is connected but no events for {eventAge.TotalMinutes:F0} min (last event at {lastEvent.Value:yyyy-MM-dd HH:mm:ss} UTC)"))
            return;

        lock (_lock) { _lastIngestStallAlert = now; }

        logger.LogWarning("Health check alert: ingest stall, last event {MinutesAgo:F0} min ago", eventAge.TotalMinutes);
    }

    private async Task CheckCrashLoopAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now)
    {
        var windowStart = now.AddMinutes(-30);
        var recentRestarts = await db.BotDowntimeIntervals
            .Where(d => d.StartedAtUtc > windowStart && d.EndedAtUtc != null
                && d.Type != Data.Entities.Core.BotDowntimeType.GracefulShutdown
                && d.Type != Data.Entities.Core.BotDowntimeType.Deploy)
            .CountAsync();

        if (recentRestarts < 3)
            return;

        lock (_lock)
        {
            if ((now - _lastCrashLoopAlert).TotalMinutes < opts.AlertCooldownMinutes)
                return;
        }

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Possible crash-loop** — {recentRestarts} restarts in the last 30 minutes. Check container logs for `Stack overflow` or other fatal errors."))
            return;

        lock (_lock) { _lastCrashLoopAlert = now; }

        logger.LogWarning("Health check alert: {Restarts} restarts in last 30 min — possible crash-loop", recentRestarts);
    }

    private async Task CheckEventTypeRatioAsync(DiscordDbContext db, HealthCheckOptions opts, DateTime now)
    {
        var baselineStart = now.AddDays(-opts.EventRatioBaselineDays);
        var recentStart = now.AddHours(-opts.EventRatioRecentHours);

        var baseline = await db.RawEventLogs
            .Where(r => r.ReceivedAtUtc > baselineStart)
            .GroupBy(r => r.EventType)
            .Select(g => new { EventType = g.Key, DailyAvg = (double)g.Count() / opts.EventRatioBaselineDays })
            .ToListAsync();

        var recent = await db.RawEventLogs
            .Where(r => r.ReceivedAtUtc > recentStart)
            .GroupBy(r => r.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EventType, g => g.Count);

        var dropped = baseline
            .Where(b => b.DailyAvg >= opts.EventRatioMinDailyBaseline)
            .Where(b =>
            {
                var recentCount = recent.GetValueOrDefault(b.EventType, 0);
                var expectedInWindow = b.DailyAvg * opts.EventRatioRecentHours / 24.0;
                return recentCount < expectedInWindow * opts.EventRatioDropThreshold;
            })
            .ToList();

        if (dropped.Count == 0)
            return;

        lock (_lock)
        {
            if ((now - _lastEventRatioAlert).TotalMinutes < opts.AlertCooldownMinutes)
                return;
        }

        var details = string.Join("\n", dropped.Select(d =>
        {
            var recentCount = recent.GetValueOrDefault(d.EventType, 0);
            var expectedInWindow = d.DailyAvg * opts.EventRatioRecentHours / 24.0;
            return $"- `{d.EventType}`: {recentCount} in last {opts.EventRatioRecentHours}h (expected ~{expectedInWindow:F0} based on {d.DailyAvg:F1}/day baseline)";
        }));

        if (!await SendWebhookAsync(opts.WebhookUrl!,
            $"**Event type ratio drop** — {dropped.Count} event type(s) below {opts.EventRatioDropThreshold:P0} of baseline:\n{details}"))
            return;

        lock (_lock) { _lastEventRatioAlert = now; }

        logger.LogWarning("Health check alert: event type ratio drop for {Types}",
            string.Join(", ", dropped.Select(d => d.EventType)));
    }

    private async Task<bool> SendWebhookAsync(string webhookUrl, string message)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = WebhookTimeout;
            var payload = JsonSerializer.Serialize(new { content = message });
            using var response = await client.PostAsync(webhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Webhook POST failed: {StatusCode} {Body}",
                    response.StatusCode, await response.Content.ReadAsStringAsync());
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
