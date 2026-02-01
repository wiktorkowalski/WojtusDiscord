using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class StickersBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<StickersBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Stickers;

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
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill stickers", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            checkpoint.TotalCount = guild.Stickers.Count;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var sticker in guild.Stickers.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tags = sticker.Tags != null ? string.Join(",", sticker.Tags) : null;

                var rowsAffected = await db.Stickers
                    .Where(s => s.DiscordId == sticker.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(st => st.Name, sticker.Name ?? string.Empty)
                        .SetProperty(st => st.Description, sticker.Description)
                        .SetProperty(st => st.Tags, tags)
                        .SetProperty(st => st.Type, (int)sticker.Type)
                        .SetProperty(st => st.FormatType, (int)sticker.FormatType)
                        .SetProperty(st => st.IsAvailable, true)
                        .SetProperty(st => st.IsDeleted, false),
                    cancellationToken);

                if (rowsAffected == 0)
                {
                    try
                    {
                        db.Stickers.Add(new StickerEntity
                        {
                            DiscordId = sticker.Id,
                            GuildId = guildEntity.Id,
                            PackId = sticker.PackId,
                            Name = sticker.Name ?? string.Empty,
                            Description = sticker.Description,
                            Tags = tags,
                            Type = (int)sticker.Type,
                            FormatType = (int)sticker.FormatType,
                            IsAvailable = true
                        });
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        logger.LogDebug("Sticker {StickerId} already exists (race condition), skipping insert", sticker.Id);
                        db.ChangeTracker.Clear();
                    }
                }

                checkpoint.ProcessedCount++;

                if (checkpoint.ProcessedCount % 50 == 0)
                {
                    await SaveProgressAsync(db, checkpoint);
                }
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("Stickers backfill completed for guild {GuildId}: {Count} stickers", guildId, checkpoint.ProcessedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stickers backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }
}
