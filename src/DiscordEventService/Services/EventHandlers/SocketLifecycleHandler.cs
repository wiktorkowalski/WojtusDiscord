using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Infrastructure;
using DiscordEventService.Jobs;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class SocketLifecycleHandler(
    DowntimeTrackerService tracker,
    GuildBackfillOrchestrator orchestrator,
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
                await tracker.OpenDowntimeAsync(
                    BotDowntimeType.GatewayDisconnect,
                    BotDowntimeDetectionMethod.GatewayEvent,
                    $"socket closed: code={e.CloseCode} message={e.CloseMessage}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to open downtime row on SocketClosed with close code {CloseCode}", e.CloseCode);
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
                // Ready can fire without a preceding Resumed (session invalidated), so close
                // any open GatewayDisconnect row before reading it back as the gap start.
                await tracker.CloseOpenDowntimeAsync(DateTime.UtcNow, onlyType: BotDowntimeType.GatewayDisconnect);
                var lastAlive = await tracker.ResolveGapStartAsync(BootClock.StartedAtUtc);
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
                        "GuildDownloadCompleted: gap {GapDuration:c} below threshold, running quick-sync only",
                        gap);
                    foreach (var guildId in e.Guilds.Keys)
                        await quickSyncService.SyncAsync(guildId);
                    return;
                }

                await EnqueueReconnectBackfillsAsync(e.Guilds.Keys, lastAlive.Value, now, gap);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enqueue reconnect backfill on GuildDownloadCompleted");
            }
        }
    }

    private async Task EnqueueReconnectBackfillsAsync(
        IEnumerable<ulong> guildIds,
        DateTime lastAlive,
        DateTime now,
        TimeSpan gap)
    {
        var earliestAllowed = now - MaxReconnectBackfillWindow;
        var afterTimestamp = lastAlive - ReconnectBackfillBuffer;
        if (afterTimestamp < earliestAllowed)
        {
            logger.LogInformation(
                "Reconnect backfill window capped to {Window} (gap was {GapDuration:c}, capped from {Original:O} to {Capped:O})",
                MaxReconnectBackfillWindow, gap, afterTimestamp, earliestAllowed);
            afterTimestamp = earliestAllowed;
        }

        // The orchestrator is the single guard against overlapping chains (#289) — it skips the
        // guild (returns null) when a chain is already active and logs why.
        foreach (var guildId in guildIds)
        {
            var jobId = await orchestrator.EnqueueBackfillFromAsync(guildId, afterTimestamp);
            if (jobId is not null)
                logger.LogInformation(
                    "Reconnect backfill enqueued for guild {GuildId} after gap {GapDuration:c}, backfilling from {AfterTimestampUtc:O}",
                    guildId, gap, afterTimestamp);
        }
    }
}
