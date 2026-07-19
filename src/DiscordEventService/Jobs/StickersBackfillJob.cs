using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

internal sealed class StickersBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor) : BackfillJobBase, IBackfillJob
{
    private const int SaveProgressInterval = 50;

    protected override BackfillType BackfillType => BackfillType.Stickers;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var stickerUpsert = ctx.Services.GetRequiredService<StickerUpsertService>();
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            ctx.Checkpoint.TotalCount = guild.Stickers.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            foreach (var sticker in guild.Stickers.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await stickerUpsert.UpsertStickerAsync(sticker, guildEntity.Id, cancellationToken);

                ctx.Checkpoint.ProcessedCount++;

                if (ctx.Checkpoint.ProcessedCount % SaveProgressInterval == 0)
                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint, cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
