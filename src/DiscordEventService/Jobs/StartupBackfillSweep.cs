using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

// #288: no backfill chain survives a process restart. A Pending/InProgress checkpoint at boot
// belongs to a dead run — but its Hangfire jobs DO survive in Postgres storage and would be
// re-fetched once the sliding invisibility timeout expires, interleaving with the reconnect
// chain that fires right after boot. So the sweep flips the checkpoint to Failed AND deletes
// the recorded job, and must run before the Hangfire server starts processing.
internal sealed class StartupBackfillSweep(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobClient backgroundJobClient,
    ILogger<StartupBackfillSweep> logger)
{
    public async Task SweepAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

        var deadCheckpoints = await db.BackfillCheckpoints
            .Where(c => c.Status == BackfillStatus.InProgress || c.Status == BackfillStatus.Pending)
            .ToListAsync();

        if (deadCheckpoints.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var checkpoint in deadCheckpoints)
        {
            if (!string.IsNullOrEmpty(checkpoint.HangfireJobId))
                TryDeleteJob(checkpoint);
            else
                logger.LogWarning(
                    "Startup sweep: no Hangfire job id recorded on {BackfillType} checkpoint for guild {GuildId}; " +
                    "an orphaned job may still re-run after the invisibility timeout",
                    checkpoint.Type, checkpoint.GuildDiscordId);

            logger.LogWarning(
                "Startup sweep: marking {Status} {BackfillType} checkpoint for guild {GuildId} as Failed (job {JobId}, last progress {LastUpdatedUtc:O})",
                checkpoint.Status, checkpoint.Type, checkpoint.GuildDiscordId,
                checkpoint.HangfireJobId ?? "none", checkpoint.LastUpdatedUtc);

            checkpoint.Status = BackfillStatus.Failed;
            checkpoint.ErrorCount++;
            checkpoint.LastError = "Interrupted by service restart (startup sweep)";
            checkpoint.LastErrorAtUtc = now;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Startup sweep: failed {Count} dead backfill checkpoint(s)", deadCheckpoints.Count);
    }

    // Delete throws when the job no longer exists in Hangfire storage (e.g. already expired) —
    // then there is nothing to delete and the checkpoint must still be failed, not the boot.
    private void TryDeleteJob(BackfillCheckpointEntity checkpoint)
    {
        try
        {
            backgroundJobClient.Delete(checkpoint.HangfireJobId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Startup sweep: could not delete Hangfire job {JobId} for {BackfillType} checkpoint of guild {GuildId}",
                checkpoint.HangfireJobId, checkpoint.Type, checkpoint.GuildDiscordId);
        }
    }
}
