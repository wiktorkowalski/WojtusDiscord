using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

internal sealed class DowntimeTrackerService(DiscordDbContext db, ILogger<DowntimeTrackerService> logger)
{
    private static readonly TimeSpan StartupGapThreshold = TimeSpan.FromSeconds(30);

    public async Task<OpenDowntimeResult> OpenDowntimeAsync(
        BotDowntimeType type,
        BotDowntimeDetectionMethod method,
        string? notes,
        DateTime? lastEventBeforeUtc = null)
    {
        var existingOpen = await db.BotDowntimeIntervals
            .Where(x => x.EndedAtUtc == null)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync();

        if (existingOpen is not null)
        {
            logger.LogWarning(
                "Skipped opening downtime row of type {DowntimeType} via {DetectionMethod}: open row {DowntimeId} with type {ExistingType} already exists",
                type, method, existingOpen.Id, existingOpen.Type);
            return new OpenDowntimeResult(existingOpen.Id, existingOpen.Type, Created: false);
        }

        var row = new BotDowntimeIntervalEntity
        {
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = null,
            Type = type,
            DetectionMethod = method,
            LastEventBeforeUtc = lastEventBeforeUtc,
            Notes = notes
        };
        db.BotDowntimeIntervals.Add(row);
        await db.SaveChangesAsync();
        logger.LogInformation(
            "Opened downtime row {DowntimeId} with type {DowntimeType} via {DetectionMethod}",
            row.Id, type, method);
        return new OpenDowntimeResult(row.Id, type, Created: true);
    }

    public async Task<int> CloseOpenDowntimeAsync(DateTime endedAtUtc, BotDowntimeType? onlyType = null)
    {
        // FirstEventAfterUtc intentionally left null here; it represents the timestamp
        // of the first real Discord event seen after recovery, not the moment we
        // observed startup. Populate it later if/when we wire that signal.
        // ExecuteUpdateAsync bypasses the SaveChangesAsync ITimestamped hook, so
        // LastUpdatedUtc must be set explicitly.
        // onlyType scopes the close so a routine SessionResumed doesn't clobber a
        // manually-opened Deploy/HostDown row from the ops endpoint.
        var query = db.BotDowntimeIntervals.Where(x => x.EndedAtUtc == null);
        if (onlyType.HasValue)
            query = query.Where(x => x.Type == onlyType.Value);

        var affected = await query.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.EndedAtUtc, endedAtUtc)
            .SetProperty(x => x.LastUpdatedUtc, endedAtUtc));

        if (affected > 1)
            logger.LogWarning("Closed {RowCount} open downtime rows (expected at most 1)", affected);
        else if (affected == 1)
            logger.LogInformation("Closed open downtime row at {EndedAtUtc:O} with type filter {TypeFilter}", endedAtUtc, onlyType?.ToString() ?? "any");
        return affected;
    }

    public async Task<Guid> RecordDbUnreachableAsync(UnwritableWindow window)
    {
        // Inserted already closed (like InferStartupGapAsync), never opened+closed: the
        // open could not have been written while the DB was down (#320).
        var row = new BotDowntimeIntervalEntity
        {
            StartedAtUtc = window.StartedAtUtc,
            EndedAtUtc = window.EndedAtUtc,
            Type = BotDowntimeType.DbUnreachable,
            DetectionMethod = BotDowntimeDetectionMethod.HeartbeatWriteFailure,
            // "failed writes", not "ticks": a hung write can eat several tick periods, so
            // the count is not duration/5s.
            Notes = $"Heartbeat writes failed for {(window.EndedAtUtc - window.StartedAtUtc).TotalSeconds:F0}s"
                + $" ({window.FailedWriteCount} failed writes) with the gateway connected"
        };
        db.BotDowntimeIntervals.Add(row);
        await db.SaveChangesAsync();
        logger.LogWarning(
            "Recorded DbUnreachable downtime {DowntimeId}: {FailedWriteCount} heartbeat writes failed over {DurationSeconds:F0}s",
            row.Id, window.FailedWriteCount, (window.EndedAtUtc - window.StartedAtUtc).TotalSeconds);
        return row.Id;
    }

    public async Task RecordHeartbeatAsync(
        DateTime nowUtc,
        bool? isGatewayConnected = null,
        int? gatewayLatencyMs = null)
    {
        // Append-only: every tick is a row. "Most recent" lookups use the
        // index on LastHeartbeatUtc. Retaining all ticks keeps a permanent
        // uptime + gateway-state history (~6M rows/year at 5s ticks).
        db.BotHeartbeats.Add(new BotHeartbeatEntity
        {
            LastHeartbeatUtc = nowUtc,
            IsGatewayConnected = isGatewayConnected,
            GatewayLatencyMs = gatewayLatencyMs
        });
        await db.SaveChangesAsync();
    }

    // beforeUtc: only signals strictly older than this instant count. Callers that run once
    // the gateway is back pass the boot instant, so this process's own heartbeats and freshly
    // logged events cannot mask the gap they are measuring (#350).
    public async Task<LastAliveResult> GetLastAliveAtUtcAsync(DateTime? beforeUtc = null)
    {
        // Heartbeat is the primary signal: it ticks regardless of Discord activity,
        // so it survives quiet periods that would leave raw_event_logs stale.
        // Queries are sequential because DbContext is not thread-safe.
        //
        // Filter to IsGatewayConnected == true so an in-process session-invalidation
        // (DSharpPlus drops, host keeps running, heartbeats keep ticking with
        // IsGatewayConnected=false) does not register as "alive" — otherwise the
        // reconnect-driven backfill always sees gap < 5s and no-ops. NULL values
        // (rows from before the gateway columns existed) are excluded by == true.
        var heartbeats = db.BotHeartbeats.Where(h => h.IsGatewayConnected == true);
        if (beforeUtc.HasValue)
            heartbeats = heartbeats.Where(h => h.LastHeartbeatUtc < beforeUtc.Value);

        var lastHeartbeat = await heartbeats
            .OrderByDescending(h => h.LastHeartbeatUtc)
            .Select(h => (DateTime?)h.LastHeartbeatUtc)
            .FirstOrDefaultAsync();

        // AsQueryable() because the filter below is conditional: without it the var is a
        // DbSet and the reassignment will not compile. Heartbeats need none — Where already
        // widened them.
        var events = db.RawEventLogs.AsQueryable();
        if (beforeUtc.HasValue)
            events = events.Where(r => r.ReceivedAtUtc < beforeUtc.Value);

        var maxReceivedAt = await events
            .OrderByDescending(r => r.ReceivedAtUtc)
            .Select(r => (DateTime?)r.ReceivedAtUtc)
            .FirstOrDefaultAsync();

        return new LastAliveResult(MaxNullable(lastHeartbeat, maxReceivedAt), lastHeartbeat, maxReceivedAt);
    }

    // Where the current gap starts, for sizing a reconnect backfill. Null = no prior signal
    // at all (first run ever). bootStartedAtUtc is BootClock.StartedAtUtc in production.
    public async Task<DateTime?> ResolveGapStartAsync(DateTime bootStartedAtUtc)
    {
        // A closed row wins over the live signals because reading heartbeats or
        // raw_event_logs unbounded here races the post-reconnect data: AllShardsConnected is
        // already true, so both show fresh timestamps that mask the real gap. Every downtime
        // classification lands in such a row — GatewayDisconnect from SocketClosed,
        // GracefulShutdown/Deploy from StopAsync, Inferred from InferStartupGapAsync.
        //
        // #350: only rows closed at or after boot qualify. An older row cannot describe the
        // gap this boot is recovering from, and trusting one did exactly that — when
        // `compose up -d` recreated Postgres first, StopAsync could not write its row, the
        // sub-threshold startup gap inferred nothing, and this reached back to a row 33 days
        // old, turning every deploy into a 2-day crawl.
        var mostRecentGapStart = await db.BotDowntimeIntervals
            .Where(x => x.EndedAtUtc != null && x.EndedAtUtc >= bootStartedAtUtc)
            .OrderByDescending(x => x.EndedAtUtc)
            .Select(x => (DateTime?)x.StartedAtUtc)
            .FirstOrDefaultAsync();

        if (mostRecentGapStart is not null)
            return mostRecentGapStart;

        // No row for this boot: fall back to the last signal from before this process
        // started. The bound is what makes the fallback safe to reach at all.
        var lastAlive = (await GetLastAliveAtUtcAsync(beforeUtc: bootStartedAtUtc)).LastAliveUtc;

        // Null means first run ever, and the caller already logs that — saying it twice is noise.
        if (lastAlive is not null)
            logger.LogInformation(
                "No downtime row closed since boot at {BootStartedAtUtc:O}; resolved gap start from pre-boot signals: {LastAliveUtc:O}",
                bootStartedAtUtc, lastAlive);
        return lastAlive;
    }

    public async Task<Guid?> InferStartupGapAsync()
    {
        var now = DateTime.UtcNow;
        var result = await GetLastAliveAtUtcAsync();
        if (result.LastAliveUtc is null)
            return null;

        var gap = now - result.LastAliveUtc.Value;
        if (gap < StartupGapThreshold)
            return null;

        var row = new BotDowntimeIntervalEntity
        {
            StartedAtUtc = result.LastAliveUtc.Value,
            EndedAtUtc = now,
            Type = BotDowntimeType.Inferred,
            DetectionMethod = BotDowntimeDetectionMethod.StartupGapInference,
            LastEventBeforeUtc = result.LastAliveUtc.Value,
            FirstEventAfterUtc = now,
            Notes = $"Startup gap inference: {gap.TotalSeconds:F0}s (heartbeat={result.LastHeartbeatUtc:O}, event={result.MaxReceivedAtUtc:O})"
        };
        db.BotDowntimeIntervals.Add(row);
        await db.SaveChangesAsync();
        logger.LogInformation(
            "Inferred startup gap of {GapSeconds:F0}s, opened downtime row {DowntimeId}",
            gap.TotalSeconds, row.Id);
        return row.Id;
    }

    private static DateTime? MaxNullable(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value > b.Value ? a : b;
    }
}

internal sealed record LastAliveResult(DateTime? LastAliveUtc, DateTime? LastHeartbeatUtc, DateTime? MaxReceivedAtUtc);

internal sealed record OpenDowntimeResult(Guid Id, BotDowntimeType ActualType, bool Created);

