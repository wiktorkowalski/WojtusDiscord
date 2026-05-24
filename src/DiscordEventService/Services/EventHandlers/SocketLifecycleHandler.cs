using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class SocketLifecycleHandler(
    IServiceScopeFactory scopeFactory,
    BootQuickSyncService quickSyncService,
    ILogger<SocketLifecycleHandler> logger) :
    IEventHandler<SocketClosedEventArgs>,
    IEventHandler<SessionResumedEventArgs>,
    IEventHandler<GuildDownloadCompletedEventArgs>
{
    private static readonly TimeSpan ReconnectGapThreshold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconnectBackfillBuffer = TimeSpan.FromMinutes(5);
    // Cap reconnect backfill to 2 days max. Longer gaps are covered by the
    // weekly periodic backfill (2-week window). Prevents 30-minute reaction
    // crawls on every deploy after a long outage.
    private static readonly TimeSpan MaxReconnectBackfillWindow = TimeSpan.FromDays(2);

    public async Task HandleEventAsync(DiscordClient sender, SocketClosedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
                await tracker.OpenDowntimeAsync(
                    BotDowntimeType.GatewayDisconnect,
                    BotDowntimeDetectionMethod.GatewayEvent,
                    $"socket closed: code={e.CloseCode} message={e.CloseMessage}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to open downtime row on SocketClosed (code={CloseCode})", e.CloseCode);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, SessionResumedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
                // Scope to GatewayDisconnect so a routine resume doesn't clobber a
                // manually-opened Deploy/HostDown row from the ops endpoint.
                await tracker.CloseOpenDowntimeAsync(DateTime.UtcNow, onlyType: BotDowntimeType.GatewayDisconnect);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to close downtime row on SessionResumed");
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildDownloadCompletedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            // Cold connect (Ready → guild download complete). Events that happened
            // while we were disconnected are NOT replayed by Discord, so we need to
            // backfill messages/reactions across the gap. Resume-paths (warm
            // reconnect) hit SessionResumed instead and don't reach here.
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<GuildBackfillOrchestrator>();
                var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();

                // Ready can fire without a preceding Resumed (session invalidated).
                await tracker.CloseOpenDowntimeAsync(DateTime.UtcNow, onlyType: BotDowntimeType.GatewayDisconnect);

                // The just-closed downtime row's StartedAtUtc is the authoritative
                // gap start. Reading heartbeats or raw_event_logs here would race
                // the post-reconnect data — by now AllShardsConnected is true, the
                // 5s heartbeat may already have written an IsGatewayConnected=true
                // row, and DSharpPlus has started dispatching events. Both signals
                // would show fresh timestamps that mask the real gap.
                //
                // The closed row covers every downtime classification:
                // SocketClosed wrote a GatewayDisconnect (in-process session
                // invalidation); StopAsync wrote a GracefulShutdown/Deploy
                // (bot restart); InferStartupGapAsync wrote an Inferred
                // (hard crash / power loss).
                var mostRecentGapStart = await db.BotDowntimeIntervals
                    .Where(x => x.EndedAtUtc != null)
                    .OrderByDescending(x => x.EndedAtUtc)
                    .Select(x => (DateTime?)x.StartedAtUtc)
                    .FirstOrDefaultAsync();

                // Fall back to the heartbeat-based heuristic only when no downtime
                // row exists at all (very first run, no prior shutdown). The
                // IsGatewayConnected==true filter inside GetLastAliveAtUtcAsync
                // keeps this conservative even in the fallback case.
                var lastAlive = mostRecentGapStart
                    ?? (await tracker.GetLastAliveAtUtcAsync()).LastAliveUtc;
                if (lastAlive is null)
                {
                    logger.LogInformation("GuildDownloadCompleted: no prior signal, skipping backfill (first run)");
                    return;
                }

                var now = DateTime.UtcNow;
                var gap = now - lastAlive.Value;
                if (gap < ReconnectGapThreshold)
                {
                    logger.LogInformation(
                        "GuildDownloadCompleted: gap {Gap:c} below threshold, running quick-sync only",
                        gap);
                    foreach (var guildId in e.Guilds.Keys)
                    {
                        await quickSyncService.SyncAsync(guildId);
                    }
                    return;
                }

                var earliestAllowed = now - MaxReconnectBackfillWindow;
                var afterTimestamp = lastAlive.Value - ReconnectBackfillBuffer;
                if (afterTimestamp < earliestAllowed)
                {
                    logger.LogInformation(
                        "Reconnect backfill window capped to {Window} (gap was {Gap:c}, capped from {Original:O} to {Capped:O})",
                        MaxReconnectBackfillWindow, gap, afterTimestamp, earliestAllowed);
                    afterTimestamp = earliestAllowed;
                }

                var inProgressGuilds = await db.BackfillCheckpoints
                    .Where(c => c.Status == BackfillStatus.InProgress)
                    .Select(c => c.GuildDiscordId)
                    .Distinct()
                    .ToListAsync();
                var inProgressSet = inProgressGuilds.ToHashSet();

                foreach (var guildId in e.Guilds.Keys)
                {
                    if (inProgressSet.Contains(guildId))
                    {
                        logger.LogInformation(
                            "Reconnect backfill skipped for guild {GuildId}: backfill already in progress",
                            guildId);
                        continue;
                    }

                    logger.LogInformation(
                        "Reconnect backfill enqueued for guild {GuildId} after gap {Gap:c} (afterTimestampUtc={After:O})",
                        guildId, gap, afterTimestamp);
                    orchestrator.EnqueueBackfillFrom(guildId, afterTimestamp);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enqueue reconnect backfill on GuildDownloadCompleted");
            }
        }
    }
}
