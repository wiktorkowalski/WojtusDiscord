using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public class DowntimeTrackerService(DiscordDbContext db, ILogger<DowntimeTrackerService> logger)
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
                "OpenDowntimeAsync({Type}/{Method}) skipped: open row Id={Id} Type={ExistingType} already exists",
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
            "Opened downtime row Id={Id} Type={Type} Method={Method}",
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
        {
            query = query.Where(x => x.Type == onlyType.Value);
        }

        var affected = await query.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.EndedAtUtc, endedAtUtc)
            .SetProperty(x => x.LastUpdatedUtc, endedAtUtc));

        if (affected > 1)
        {
            logger.LogWarning("Closed {Count} open downtime rows (expected at most 1)", affected);
        }
        else if (affected == 1)
        {
            logger.LogInformation("Closed open downtime row at {EndedAt:O} (filter={Filter})", endedAtUtc, onlyType?.ToString() ?? "any");
        }
        return affected;
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

    public async Task<LastAliveResult> GetLastAliveAtUtcAsync()
    {
        // Heartbeat is the primary signal: it ticks regardless of Discord activity,
        // so it survives quiet periods that would leave raw_event_logs stale.
        // Queries are sequential because DbContext is not thread-safe.
        var lastHeartbeat = await db.BotHeartbeats
            .OrderByDescending(h => h.LastHeartbeatUtc)
            .Select(h => (DateTime?)h.LastHeartbeatUtc)
            .FirstOrDefaultAsync();

        var maxReceivedAt = await db.RawEventLogs
            .OrderByDescending(r => r.ReceivedAtUtc)
            .Select(r => (DateTime?)r.ReceivedAtUtc)
            .FirstOrDefaultAsync();

        return new LastAliveResult(MaxNullable(lastHeartbeat, maxReceivedAt), lastHeartbeat, maxReceivedAt);
    }

    public async Task<Guid?> InferStartupGapAsync()
    {
        var now = DateTime.UtcNow;
        var result = await GetLastAliveAtUtcAsync();
        if (result.LastAliveUtc is null)
        {
            return null;
        }

        var gap = now - result.LastAliveUtc.Value;
        if (gap < StartupGapThreshold)
        {
            return null;
        }

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
            "Inferred startup gap of {Gap:F0}s, row Id={Id}",
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

public record LastAliveResult(DateTime? LastAliveUtc, DateTime? LastHeartbeatUtc, DateTime? MaxReceivedAtUtc);

public record OpenDowntimeResult(Guid Id, BotDowntimeType ActualType, bool Created);

