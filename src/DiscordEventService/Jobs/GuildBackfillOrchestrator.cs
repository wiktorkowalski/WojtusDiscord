using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

internal sealed class GuildBackfillOrchestrator(
    DiscordDbContext db,
    IBackgroundJobClient backgroundJobClient,
    ILogger<GuildBackfillOrchestrator> logger)
{
    // Delay before the first chain job so the Pending checkpoint rows (with their Hangfire job
    // ids) are committed before any job starts flipping statuses — otherwise a fast worker could
    // write InProgress that the enqueue transaction then overwrites with Pending.
    private static readonly TimeSpan FirstJobDelay = TimeSpan.FromSeconds(5);

    public Task<string?> StartBackfillAsync(ulong guildId, BackfillOptions? options = null)
        => EnqueueChainAsync(guildId, options ?? BackfillOptions.Default, afterTimestampUtc: null);

    // afterTimestampUtc stops Messages/Reactions scrolling once older than the window;
    // used by reconnect-driven and historical-gap-driven backfills.
    public Task<string?> EnqueueBackfillFromAsync(ulong guildId, DateTime afterTimestampUtc, BackfillOptions? options = null)
        => EnqueueChainAsync(guildId, options ?? BackfillOptions.Default, afterTimestampUtc);

    // Single authoritative guard against overlapping chains for a guild (#289): the check and the
    // enqueue happen under a per-guild Postgres advisory lock, so two concurrent triggers (manual
    // endpoint, reconnect, periodic) cannot both pass the check. Returns null when skipped.
    private async Task<string?> EnqueueChainAsync(ulong guildId, BackfillOptions options, DateTime? afterTimestampUtc)
    {
        // Session-level advisory lock on a pinned connection, not pg_advisory_xact_lock: the
        // retrying execution strategy rejects user-initiated transactions, and wrapping this whole
        // block in the strategy could re-run the (non-transactional) Hangfire enqueue on a
        // transient retry — the exact double-chain this guard exists to prevent. One bigint key
        // per guild id; nothing else in this app takes advisory locks.
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({unchecked((long)guildId)})");
            return await EnqueueChainLockedAsync(guildId, options, afterTimestampUtc);
        }
        finally
        {
            try
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock_all()");
            }
            catch
            {
                // A dropped connection has already released the session lock server-side.
            }
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<string?> EnqueueChainLockedAsync(ulong guildId, BackfillOptions options, DateTime? afterTimestampUtc)
    {
        var now = DateTime.UtcNow;
        var checkpoints = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId && c.Type != BackfillType.MemeIndex)
            .ToListAsync();

        foreach (var stale in checkpoints.Where(c =>
                     c.Status == BackfillStatus.InProgress && !c.IsChainActive(now)))
        {
            logger.LogWarning(
                "Ignoring stale InProgress {BackfillType} checkpoint for guild {GuildId}: no progress since {LastUpdatedUtc:O}",
                stale.Type, stale.GuildDiscordId, stale.LastUpdatedUtc);
        }

        if (checkpoints.Any(c => c.IsChainActive(now)))
        {
            logger.LogInformation("Backfill skipped for guild {GuildId}: a backfill chain is already active", guildId);
            return null;
        }

        logger.LogInformation(
            "Starting backfill orchestration for guild {GuildId} with options {@Options}, backfilling after {AfterTimestampUtc:O}",
            guildId, options, afterTimestampUtc);

        var chain = EnqueueChainJobs(guildId, options, afterTimestampUtc);
        RecordChainOnCheckpoints(checkpoints, guildId, chain, now);
        await db.SaveChangesAsync();

        logger.LogInformation("Backfill orchestration started for guild {GuildId}, first job: {JobId}, final job: {FinalJobId}",
            guildId, chain[0].JobId, chain[^1].JobId);

        return chain[0].JobId;
    }

    private List<(BackfillType Type, string JobId)> EnqueueChainJobs(
        ulong guildId, BackfillOptions options, DateTime? afterTimestampUtc)
    {
        List<(BackfillType Type, string JobId)> chain = [];

        var rolesJobId = backgroundJobClient.Schedule<RolesBackfillJob>(
            j => j.ExecuteAsync(guildId, CancellationToken.None), FirstJobDelay);
        chain.Add((BackfillType.Roles, rolesJobId));

        var emojisJobId = backgroundJobClient.ContinueJobWith<EmojisBackfillJob>(
            rolesJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));
        chain.Add((BackfillType.Emojis, emojisJobId));

        var stickersJobId = backgroundJobClient.ContinueJobWith<StickersBackfillJob>(
            emojisJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));
        chain.Add((BackfillType.Stickers, stickersJobId));

        var channelsJobId = backgroundJobClient.ContinueJobWith<ChannelsBackfillJob>(
            stickersJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));
        chain.Add((BackfillType.Channels, channelsJobId));

        var membersJobId = backgroundJobClient.ContinueJobWith<MembersBackfillJob>(
            channelsJobId, j => j.ExecuteAsync(guildId, CancellationToken.None));
        chain.Add((BackfillType.Members, membersJobId));

        if (!options.IncludeMessages)
            return chain;

        var messagesJobId = backgroundJobClient.ContinueJobWith<MessagesBackfillJob>(
            membersJobId, j => j.ExecuteAsync(guildId, afterTimestampUtc, CancellationToken.None));
        chain.Add((BackfillType.Messages, messagesJobId));

        if (options.IncludeReactions)
        {
            var reactionsJobId = backgroundJobClient.ContinueJobWith<ReactionsBackfillJob>(
                messagesJobId, j => j.ExecuteAsync(guildId, afterTimestampUtc, CancellationToken.None));
            chain.Add((BackfillType.Reactions, reactionsJobId));
        }

        return chain;
    }

    // Every chain job gets its Hangfire job id durably recorded before it runs, so the startup
    // sweep (#288) and the cancel endpoint can delete the real jobs. Stale-InProgress rows keep
    // their status — the executor resumes from the cursor only when Status==InProgress (#282);
    // the SaveChanges heartbeat bump alone re-arms the chain guard for them.
    private void RecordChainOnCheckpoints(
        List<BackfillCheckpointEntity> checkpoints,
        ulong guildId,
        List<(BackfillType Type, string JobId)> chain,
        DateTime now)
    {
        foreach (var (type, jobId) in chain)
        {
            var checkpoint = checkpoints.FirstOrDefault(c => c.Type == type);
            if (checkpoint is null)
            {
                checkpoint = new BackfillCheckpointEntity
                {
                    GuildDiscordId = guildId,
                    Type = type,
                    StartedAtUtc = now
                };
                db.BackfillCheckpoints.Add(checkpoint);
            }

            if (checkpoint.Status != BackfillStatus.InProgress)
            {
                checkpoint.Status = BackfillStatus.Pending;
                checkpoint.CompletedAtUtc = null;
                checkpoint.StartedAtUtc = now;
            }
            checkpoint.HangfireJobId = jobId;
        }
    }
}

internal sealed record BackfillOptions
{
    public bool IncludeMessages { get; init; } = true;
    public bool IncludeReactions { get; init; } = true;

    public static BackfillOptions Default => new BackfillOptions();
}
