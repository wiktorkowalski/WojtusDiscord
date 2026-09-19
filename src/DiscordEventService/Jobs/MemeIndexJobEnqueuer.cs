using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

// The checkpoint must carry the Hangfire job id (#312, as GuildBackfillOrchestrator does), or
// cancel / the startup sweep only flip a status that the still-running job then overwrites.
internal static class MemeIndexJobEnqueuer
{
    public static async Task<string> EnqueueAsync(
        DiscordDbContext db,
        IBackgroundJobClient jobClient,
        ulong guildId,
        bool sweep,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var checkpoint = await db.BackfillCheckpoints
            .FirstOrDefaultAsync(c => c.GuildDiscordId == guildId && c.Type == BackfillType.MemeIndex, cancellationToken);

        if (checkpoint is null)
        {
            checkpoint = new BackfillCheckpointEntity
            {
                GuildDiscordId = guildId,
                Type = BackfillType.MemeIndex,
                StartedAtUtc = now
            };
            db.BackfillCheckpoints.Add(checkpoint);
        }

        // A stale InProgress row (dead job, #293) stays InProgress so the executor resumes from
        // its cursor; any terminal row becomes Pending, which the executor flips to InProgress.
        if (checkpoint.Status != BackfillStatus.InProgress)
        {
            checkpoint.Status = BackfillStatus.Pending;
            checkpoint.CompletedAtUtc = null;
            checkpoint.StartedAtUtc = now;
        }

        // Status lands before the job exists, so the run can never race this write. The id is a
        // second save that touches only its own column: if the job already flipped the row to
        // InProgress (or finished), that status is not overwritten with Pending.
        await db.SaveChangesAsync(cancellationToken);

        var jobId = sweep
            ? jobClient.Enqueue<MemeIndexingJob>(j => j.ExecuteSweepAsync(guildId, CancellationToken.None))
            : jobClient.Enqueue<MemeIndexingJob>(j => j.ExecuteAsync(guildId, CancellationToken.None));

        checkpoint.HangfireJobId = jobId;
        await db.SaveChangesAsync(cancellationToken);
        return jobId;
    }
}
