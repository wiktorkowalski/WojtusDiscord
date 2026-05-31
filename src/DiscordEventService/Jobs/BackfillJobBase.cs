using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Jobs;

/// <summary>
/// Per-item progress helpers shared by every backfill job. The job lifecycle (scope, checkpoint
/// get-or-create, status transitions) lives in <see cref="BackfillJobExecutor"/>; only the
/// mid-loop progress/error book-keeping that runs inside a job's work delegate stays here.
/// </summary>
public abstract class BackfillJobBase
{
    protected abstract BackfillType BackfillType { get; }

    protected async Task RecordErrorAsync(DiscordDbContext db, BackfillCheckpointEntity checkpoint, Exception ex)
    {
        checkpoint.ErrorCount++;
        checkpoint.LastError = $"{ex.GetType().Name}: {ex.Message}";
        checkpoint.LastErrorAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    protected async Task SaveProgressAsync(DiscordDbContext db, BackfillCheckpointEntity checkpoint)
    {
        await db.SaveChangesAsync();
    }
}
