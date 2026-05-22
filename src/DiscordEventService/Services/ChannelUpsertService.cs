using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public class ChannelUpsertService(DiscordDbContext db, ILogger<ChannelUpsertService> logger)
{
    public async Task<Guid> UpsertChannelAsync(DiscordChannel channel, Guid guildId)
    {
        var rowsAffected = await db.Channels
            .Where(c => c.DiscordId == channel.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Name, channel.Name)
                .SetProperty(c => c.Type, (ChannelType)channel.Type)
                .SetProperty(c => c.Topic, channel.Topic)
                .SetProperty(c => c.Position, channel.Position)
                .SetProperty(c => c.ParentDiscordId, channel.ParentId)
                .SetProperty(c => c.Bitrate, channel.Bitrate)
                .SetProperty(c => c.UserLimit, channel.UserLimit)
                .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                .SetProperty(c => c.IsNsfw, channel.IsNSFW));

        if (rowsAffected == 0)
        {
            try
            {
                db.Channels.Add(new ChannelEntity
                {
                    DiscordId = channel.Id,
                    GuildId = guildId,
                    Name = channel.Name,
                    Type = (ChannelType)channel.Type,
                    Topic = channel.Topic,
                    Position = channel.Position,
                    ParentDiscordId = channel.ParentId,
                    Bitrate = channel.Bitrate,
                    UserLimit = channel.UserLimit,
                    RateLimitPerUser = channel.PerUserRateLimit,
                    IsNsfw = channel.IsNSFW,
                    IsDeleted = false
                });
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                db.ChangeTracker.Clear();
                await db.Channels
                    .Where(c => c.DiscordId == channel.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Name, channel.Name)
                        .SetProperty(c => c.Type, (ChannelType)channel.Type)
                        .SetProperty(c => c.Topic, channel.Topic)
                        .SetProperty(c => c.Position, channel.Position)
                        .SetProperty(c => c.ParentDiscordId, channel.ParentId)
                        .SetProperty(c => c.Bitrate, channel.Bitrate)
                        .SetProperty(c => c.UserLimit, channel.UserLimit)
                        .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                        .SetProperty(c => c.IsNsfw, channel.IsNSFW));
            }
        }

        var id = await db.Channels
            .Where(c => c.DiscordId == channel.Id)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        if (id == Guid.Empty)
        {
            logger.LogError("ChannelUpsert lost the row for DiscordId={DiscordId} after upsert", channel.Id);
        }

        return id;
    }

    public async Task<Guid> InsertPlaceholderAsync(ulong channelDiscordId, Guid guildId, DateTime firstOrphanSeenUtc)
    {
        logger.LogWarning(
            "Inserting placeholder channel row for unresolvable thread DiscordId={DiscordId}; first orphan message seen at {FirstSeen}",
            channelDiscordId, firstOrphanSeenUtc);

        try
        {
            var placeholder = new ChannelEntity
            {
                DiscordId = channelDiscordId,
                GuildId = guildId,
                Name = $"[unknown thread {channelDiscordId}]",
                Type = ChannelType.PublicThread,
                Position = 0,
                IsDeleted = false
            };
            db.Channels.Add(placeholder);
            await db.SaveChangesAsync();
            return placeholder.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            return await db.Channels
                .Where(c => c.DiscordId == channelDiscordId)
                .Select(c => c.Id)
                .FirstAsync();
        }
    }

    public async Task MarkDeletedAsync(ulong channelDiscordId)
    {
        await db.Channels
            .Where(c => c.DiscordId == channelDiscordId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true));
    }
}
