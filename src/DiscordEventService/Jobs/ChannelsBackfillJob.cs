using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

internal sealed class ChannelsBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor,
    ILogger<ChannelsBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    private const int SaveProgressInterval = 50;

    protected override BackfillType BackfillType => BackfillType.Channels;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();
            var guild = await discordClient.GetGuildAsync(guildId);
            var channels = await guild.GetChannelsAsync();
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            // Threads must be rows in channels too — Messages/Reactions backfill FK onto them (#283).
            var threads = await ListGuildThreadsAsync(guild, channels, logger, cancellationToken);
            var allChannels = channels
                .Concat<DSharpPlus.Entities.DiscordChannel>(threads)
                .DistinctBy(c => c.Id)
                .ToList();

            ctx.Checkpoint.TotalCount = allChannels.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            foreach (var channel in allChannels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await channelUpsert.UpsertChannelAsync(channel, guildEntity.Id, cancellationToken);

                ctx.Checkpoint.ProcessedCount++;

                if (ctx.Checkpoint.ProcessedCount % SaveProgressInterval == 0)
                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint, cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
