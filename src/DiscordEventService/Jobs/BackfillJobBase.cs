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

    /// <summary>
    /// Drives the per-item resume-cursor loop shared by the history jobs (Messages, Reactions):
    /// skip to the channel a genuinely-interrupted run stopped on (<c>CurrentChannelId</c>, already
    /// reset to null by <see cref="BackfillJobExecutor"/> after a terminal run), then walk the rest,
    /// stamping <c>CurrentChannelId</c> per item and clearing the per-channel batch cursor
    /// (<c>LastProcessedId</c>) only AFTER an item finishes — so the resume channel still sees its
    /// saved batch cursor on entry. A per-item failure (non-cancellation) is recorded and the loop
    /// continues with the next item; cancellation propagates to the executor's terminal handling.
    /// <para>
    /// <paramref name="items"/> MUST be in a stable, deterministic order across runs (the jobs
    /// guarantee this via <c>OrderBy(c =&gt; c.Id)</c>); otherwise resume would skip the wrong items.
    /// The per-batch <c>LastProcessedId</c> write stays inside <paramref name="processItem"/> — the
    /// inner paging body is job-specific and not extracted here.
    /// </para>
    /// </summary>
    protected async Task IterateWithCheckpointAsync<T>(
        DiscordDbContext db,
        BackfillCheckpointEntity checkpoint,
        IReadOnlyList<T> items,
        Func<T, ulong> keyOf,
        Func<T, Task> processItem,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startIndex = 0;
        if (checkpoint.CurrentChannelId is { } resumeId)
        {
            var found = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (keyOf(items[i]) == resumeId)
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                startIndex = found;
            }
            else
            {
                logger.LogWarning("Backfill resume cursor {ResumeId} not found in {Count} items, restarting from beginning",
                    resumeId, items.Count);
                checkpoint.CurrentChannelId = null;
                checkpoint.LastProcessedId = null;
            }
        }

        for (var i = startIndex; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[i];
            checkpoint.CurrentChannelId = keyOf(item);
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                await processItem(item);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Backfill failed for item {ItemId}, continuing with next", keyOf(item));
                await RecordErrorAsync(db, checkpoint, ex);
            }

            // Reset the per-channel batch cursor AFTER the item, so the resume channel saw its saved
            // LastProcessedId on entry and the next channel starts from the newest message.
            checkpoint.LastProcessedId = null;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
