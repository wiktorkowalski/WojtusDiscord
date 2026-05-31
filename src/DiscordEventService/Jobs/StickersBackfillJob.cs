using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public sealed class StickersBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Stickers;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            ctx.Checkpoint.TotalCount = guild.Stickers.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            foreach (var sticker in guild.Stickers.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tags = sticker.Tags != null ? string.Join(",", sticker.Tags) : null;

                await ctx.Db.Stickers.UpsertAsync(
                    s => s.DiscordId == sticker.Id,
                    s => s
                        .SetProperty(st => st.Name, sticker.Name ?? string.Empty)
                        .SetProperty(st => st.Description, sticker.Description)
                        .SetProperty(st => st.Tags, tags)
                        .SetProperty(st => st.Type, (int)sticker.Type)
                        .SetProperty(st => st.FormatType, (int)sticker.FormatType)
                        .SetProperty(st => st.IsAvailable, true)
                        .SetProperty(st => st.IsDeleted, false)
                        .SetProperty(st => st.DeletedAtUtc, (DateTime?)null),
                    () => new StickerEntity
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
                    },
                    s => s.Id,
                    cancellationToken);

                ctx.Checkpoint.ProcessedCount++;

                if (ctx.Checkpoint.ProcessedCount % 50 == 0)
                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
