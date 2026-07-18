using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
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

                await ctx.Db.Channels.UpsertAsync(
                    c => c.DiscordId == channel.Id,
                    s => s
                        .SetProperty(c => c.Name, channel.Name)
                        .SetProperty(c => c.Type, (ChannelType)(int)channel.Type)
                        .SetProperty(c => c.Topic, channel.Topic)
                        .SetProperty(c => c.Bitrate, channel.Bitrate)
                        .SetProperty(c => c.UserLimit, channel.UserLimit)
                        .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                        .SetProperty(c => c.IsNsfw, channel.IsNSFW)
                        .SetProperty(c => c.Position, channel.Position)
                        .SetProperty(c => c.ParentDiscordId, channel.ParentId)
                        .SetProperty(c => c.IsDeleted, false)
                        .SetProperty(c => c.DeletedAtUtc, (DateTime?)null),
                    () => new ChannelEntity
                    {
                        DiscordId = channel.Id,
                        GuildId = guildEntity.Id,
                        ParentDiscordId = channel.ParentId,
                        Name = channel.Name,
                        Type = (ChannelType)(int)channel.Type,
                        Topic = channel.Topic,
                        Bitrate = channel.Bitrate,
                        UserLimit = channel.UserLimit,
                        RateLimitPerUser = channel.PerUserRateLimit,
                        IsNsfw = channel.IsNSFW,
                        Position = channel.Position
                    },
                    c => c.Id,
                    cancellationToken);

                ctx.Checkpoint.ProcessedCount++;

                if (ctx.Checkpoint.ProcessedCount % SaveProgressInterval == 0)
                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint, cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);
}
