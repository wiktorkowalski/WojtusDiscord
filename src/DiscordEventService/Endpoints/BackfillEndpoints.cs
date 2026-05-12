using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Endpoints;

public static class BackfillEndpoints
{
    // Buffer one bucket back so a partial bucket at the very start of the gap
    // is still scanned end-to-end. Matches the 'interval 1 hour' bucket size
    // hard-coded in the historical-gaps SQL CTE — keep them in sync if changed.
    private static readonly TimeSpan GapBufferBeforeEarliestBucket = TimeSpan.FromHours(1);


    public static void MapBackfillEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/backfill");

        group.MapPost("/{guildId:long}", StartBackfill)
            .WithName("StartBackfill")
            .Produces<BackfillResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{guildId:long}/status", GetBackfillStatus)
            .WithName("GetBackfillStatus")
            .Produces<BackfillStatusResponse>();

        group.MapPost("/{guildId:long}/cancel", CancelBackfill)
            .WithName("CancelBackfill")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{guildId:long}/reset", ResetBackfill)
            .WithName("ResetBackfill")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/historical-gaps", BackfillHistoricalGaps)
            .WithName("BackfillHistoricalGaps")
            .Produces<HistoricalGapsResponse>(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> StartBackfill(
        ulong guildId,
        BackfillRequest? request,
        GuildBackfillOrchestrator orchestrator,
        DiscordDbContext db)
    {
        // Check if backfill already in progress
        var existingInProgress = await db.BackfillCheckpoints
            .AnyAsync(c => c.GuildDiscordId == guildId && c.Status == BackfillStatus.InProgress);

        if (existingInProgress)
        {
            return Results.BadRequest(new { error = "Backfill already in progress for this guild" });
        }

        var options = request?.ToOptions() ?? BackfillOptions.Default;
        var jobId = orchestrator.StartBackfill(guildId, options);

        return Results.Accepted(
            $"/api/backfill/{guildId}/status",
            new BackfillResponse { JobId = jobId, GuildId = guildId });
    }

    private static async Task<IResult> GetBackfillStatus(
        ulong guildId,
        DiscordDbContext db)
    {
        var checkpoints = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId)
            .OrderBy(c => c.Type)
            .ToListAsync();

        var overallStatus = checkpoints.Count == 0 ? "NotStarted" :
            checkpoints.Any(c => c.Status == BackfillStatus.InProgress) ? "InProgress" :
            checkpoints.Any(c => c.Status == BackfillStatus.Failed) ? "Failed" :
            checkpoints.All(c => c.Status == BackfillStatus.Completed) ? "Completed" :
            "Pending";

        return Results.Ok(new BackfillStatusResponse
        {
            GuildId = guildId,
            OverallStatus = overallStatus,
            Checkpoints = checkpoints.Select(c => new CheckpointDto
            {
                Type = c.Type.ToString(),
                Status = c.Status.ToString(),
                ProcessedCount = c.ProcessedCount,
                TotalCount = c.TotalCount,
                ErrorCount = c.ErrorCount,
                LastError = c.LastError,
                StartedAt = c.StartedAtUtc,
                CompletedAt = c.CompletedAtUtc
            }).ToList()
        });
    }

    private static async Task<IResult> CancelBackfill(
        ulong guildId,
        DiscordDbContext db,
        IBackgroundJobClient jobClient)
    {
        var inProgress = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId && c.Status == BackfillStatus.InProgress)
            .ToListAsync();

        foreach (var checkpoint in inProgress)
        {
            if (!string.IsNullOrEmpty(checkpoint.HangfireJobId))
            {
                jobClient.Delete(checkpoint.HangfireJobId);
            }
            checkpoint.Status = BackfillStatus.Cancelled;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetBackfill(
        ulong guildId,
        DiscordDbContext db)
    {
        var checkpoints = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId)
            .ToListAsync();

        db.BackfillCheckpoints.RemoveRange(checkpoints);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> BackfillHistoricalGaps(
        DiscordDbContext db,
        GuildBackfillOrchestrator orchestrator)
    {
        // Find the oldest hour bucket that has zero raw_event_logs rows, then
        // enqueue a full backfill from that point (minus a 1-hour buffer) for
        // every known guild. Per the user's "overshoot" guidance we don't try
        // to cover each gap with a separate run — one wide-net pass per guild
        // is simpler and the existing idempotency checks keep it safe.
        var hasAnyEvents = await db.RawEventLogs.AnyAsync();
        if (!hasAnyEvents)
        {
            return Results.Ok(new HistoricalGapsResponse
            {
                EarliestGapStartUtc = null,
                AfterTimestampUtc = null,
                EnqueuedGuildIds = [],
                SkippedGuildIds = [],
                Message = "raw_event_logs is empty; nothing to backfill"
            });
        }

        var gapRow = await db.Database
            .SqlQueryRaw<HistoricalGapRow>(@"
                WITH hours AS (
                    SELECT generate_series(
                        date_trunc('hour', (SELECT min(received_at_utc) FROM raw_event_logs)),
                        date_trunc('hour', now()),
                        interval '1 hour'
                    ) AS gap_start
                )
                SELECT gap_start AS ""GapStart"" FROM hours
                WHERE NOT EXISTS (
                    SELECT 1 FROM raw_event_logs r
                    WHERE date_trunc('hour', r.received_at_utc) = hours.gap_start
                )
                ORDER BY gap_start
                LIMIT 1")
            .FirstOrDefaultAsync();

        var earliestGapStart = gapRow?.GapStart;
        if (earliestGapStart is null)
        {
            return Results.Ok(new HistoricalGapsResponse
            {
                EarliestGapStartUtc = null,
                AfterTimestampUtc = null,
                EnqueuedGuildIds = [],
                SkippedGuildIds = [],
                Message = "No zero-event hour buckets found"
            });
        }

        var afterTimestamp = earliestGapStart.Value - GapBufferBeforeEarliestBucket;
        var guildIds = await db.Guilds
            .Where(g => g.LeftAtUtc == null)
            .Select(g => g.DiscordId)
            .ToListAsync();

        var inProgressSet = (await db.BackfillCheckpoints
            .Where(c => c.Status == BackfillStatus.InProgress)
            .Select(c => c.GuildDiscordId)
            .Distinct()
            .ToListAsync()).ToHashSet();

        var enqueued = new List<ulong>();
        var skipped = new List<ulong>();

        foreach (var guildId in guildIds)
        {
            if (inProgressSet.Contains(guildId))
            {
                skipped.Add(guildId);
                continue;
            }

            orchestrator.EnqueueBackfillFrom(guildId, afterTimestamp);
            enqueued.Add(guildId);
        }

        return Results.Accepted("/api/backfill/status", new HistoricalGapsResponse
        {
            EarliestGapStartUtc = earliestGapStart,
            AfterTimestampUtc = afterTimestamp,
            EnqueuedGuildIds = enqueued,
            SkippedGuildIds = skipped,
            Message = $"Enqueued {enqueued.Count} guild(s), skipped {skipped.Count} (already in progress)"
        });
    }
}

public record BackfillRequest
{
    public bool IncludeMessages { get; init; } = true;
    public bool IncludeReactions { get; init; } = true;

    public BackfillOptions ToOptions() => new()
    {
        IncludeMessages = IncludeMessages,
        IncludeReactions = IncludeReactions
    };
}

public record BackfillResponse
{
    public required string JobId { get; init; }
    public required ulong GuildId { get; init; }
}

public record BackfillStatusResponse
{
    public required ulong GuildId { get; init; }
    public required string OverallStatus { get; init; }
    public required List<CheckpointDto> Checkpoints { get; init; }
}

public record CheckpointDto
{
    public required string Type { get; init; }
    public required string Status { get; init; }
    public int ProcessedCount { get; init; }
    public int? TotalCount { get; init; }
    public int ErrorCount { get; init; }
    public string? LastError { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

public record HistoricalGapsResponse
{
    public DateTime? EarliestGapStartUtc { get; init; }
    public DateTime? AfterTimestampUtc { get; init; }
    public required IReadOnlyList<ulong> EnqueuedGuildIds { get; init; }
    public required IReadOnlyList<ulong> SkippedGuildIds { get; init; }
    public required string Message { get; init; }
}

public record HistoricalGapRow(DateTime? GapStart);
