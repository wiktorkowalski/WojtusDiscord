using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

internal sealed class EmojisBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor) : BackfillJobBase, IBackfillJob
{
    private const int SaveProgressInterval = 50;

    protected override BackfillType BackfillType => BackfillType.Emojis;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            ctx.Checkpoint.TotalCount = guild.Emojis.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            foreach (var emoji in guild.Emojis.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ctx.Db.Emotes.UpsertAsync(
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

                ctx.Checkpoint.ProcessedCount++;

                if (ctx.Checkpoint.ProcessedCount % SaveProgressInterval == 0)
                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint, cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
