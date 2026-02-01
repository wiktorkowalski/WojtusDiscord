using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public abstract class BackfillJobBase
{
    protected abstract BackfillType BackfillType { get; }

    protected async Task<BackfillCheckpointEntity> GetOrCreateCheckpointAsync(
        DiscordDbContext db, ulong guildId)
    {
        var checkpoint = await db.BackfillCheckpoints
            .FirstOrDefaultAsync(c => c.GuildDiscordId == guildId && c.Type == BackfillType);

        if (checkpoint is null)
        {
            checkpoint = new BackfillCheckpointEntity
            {
                GuildDiscordId = guildId,
                Type = BackfillType,
                Status = BackfillStatus.Pending,
                StartedAtUtc = DateTime.UtcNow
            };
            db.BackfillCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync();
        }

        return checkpoint;
    }

    protected async Task MarkCompletedAsync(DiscordDbContext db, BackfillCheckpointEntity checkpoint)
    {
        checkpoint.Status = BackfillStatus.Completed;
        checkpoint.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    protected async Task MarkFailedAsync(DiscordDbContext db, BackfillCheckpointEntity checkpoint, Exception ex)
    {
        checkpoint.Status = BackfillStatus.Failed;
        checkpoint.ErrorCount++;
        checkpoint.LastError = $"{ex.GetType().Name}: {ex.Message}";
        checkpoint.LastErrorAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

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
