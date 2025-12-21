using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class ChannelsBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<ChannelsBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Channels;

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
            var channels = await guild.GetChannelsAsync();
            var guildEntity = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
            {
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill channels", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            checkpoint.TotalCount = channels.Count;
            await db.SaveChangesAsync(cancellationToken);

            foreach (var channel in channels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rowsAffected = await db.Channels
                    .Where(c => c.DiscordId == channel.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Name, channel.Name)
                        .SetProperty(c => c.Type, (ChannelType)(int)channel.Type)
                        .SetProperty(c => c.Topic, channel.Topic)
                        .SetProperty(c => c.Bitrate, channel.Bitrate)
                        .SetProperty(c => c.UserLimit, channel.UserLimit)
                        .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                        .SetProperty(c => c.IsNsfw, channel.IsNSFW)
                        .SetProperty(c => c.Position, channel.Position)
                        .SetProperty(c => c.ParentDiscordId, channel.ParentId)
                        .SetProperty(c => c.IsDeleted, false),
                    cancellationToken);

                if (rowsAffected == 0)
                {
                    try
                    {
                        db.Channels.Add(new ChannelEntity
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
                        });
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        logger.LogDebug("Channel {ChannelId} already exists (race condition), skipping insert", channel.Id);
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
            logger.LogInformation("Channels backfill completed for guild {GuildId}: {Count} channels", guildId, checkpoint.ProcessedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Channels backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }
}
