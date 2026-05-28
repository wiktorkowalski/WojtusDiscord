using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class EmojisBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<EmojisBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Emojis;

    public async Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();

        var checkpoint = await GetOrCreateCheckpointAsync(db, guildId);
        checkpoint.Status = BackfillStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
            {
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill emojis", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            checkpoint.TotalCount = guild.Emojis.Count;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var emoji in guild.Emojis.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await db.Emotes.UpsertAsync(
                    e => e.DiscordId == emoji.Id,
                    s => s
                        .SetProperty(e => e.Name, emoji.Name)
                        .SetProperty(e => e.IsAnimated, emoji.IsAnimated)
                        .SetProperty(e => e.IsAvailable, emoji.IsAvailable)
                        .SetProperty(e => e.IsDeleted, false)
                        .SetProperty(e => e.DeletedAtUtc, (DateTime?)null),
                    () => new EmoteEntity
                    {
                        DiscordId = emoji.Id,
                        GuildId = guildEntity.Id,
                        Name = emoji.Name,
                        IsAnimated = emoji.IsAnimated,
                        IsAvailable = emoji.IsAvailable
                    },
                    e => e.Id,
                    cancellationToken);

                checkpoint.ProcessedCount++;

                if (checkpoint.ProcessedCount % 50 == 0)
                {
                    await SaveProgressAsync(db, checkpoint);
                }
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("Emojis backfill completed for guild {GuildId}: {Count} emojis", guildId, checkpoint.ProcessedCount);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning("Emojis backfill cancelled for guild {GuildId} (likely deploy restart)", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Emojis backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }
}
