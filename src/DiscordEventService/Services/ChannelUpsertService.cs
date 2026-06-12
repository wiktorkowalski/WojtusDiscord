using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

internal sealed class ChannelUpsertService(DiscordDbContext db, ILogger<ChannelUpsertService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertChannelAsync(DiscordChannel channel, Guid guildId)
    {
        var id = await db.Channels.UpsertAsync(
            c => c.DiscordId == channel.Id,
            s => s
                .SetProperty(c => c.Name, channel.Name)
                .SetProperty(c => c.Type, (ChannelType)channel.Type)
                .SetProperty(c => c.Topic, channel.Topic)
                .SetProperty(c => c.Position, channel.Position)
                .SetProperty(c => c.ParentDiscordId, channel.ParentId)
                .SetProperty(c => c.Bitrate, channel.Bitrate)
                .SetProperty(c => c.UserLimit, channel.UserLimit)
                .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                .SetProperty(c => c.IsNsfw, channel.IsNSFW),
            () => new ChannelEntity
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
            },
            c => c.Id);

        if (id == Guid.Empty)
        {
            logger.LogError("Channel upsert lost the row for channel {ChannelId} after upsert", channel.Id);
            return UpsertResult<Guid>.Failure($"Channel upsert lost the row for DiscordId={channel.Id}");
        }

        return UpsertResult<Guid>.Success(id);
    }

    public async Task<Guid> InsertPlaceholderAsync(ulong channelDiscordId, Guid guildId, DateTime firstOrphanSeenUtc)
    {
        logger.LogWarning(
            "Inserting placeholder channel row for unresolvable thread {ChannelId}; first orphan message seen at {FirstOrphanSeenUtc}",
            channelDiscordId, firstOrphanSeenUtc);

        // Insert-or-get: a concurrent writer (or a real ChannelCreate) may have inserted the row
        // first. On conflict we return the existing row UNTOUCHED — never overwrite a real channel
        // with placeholder values.
        var (channel, _) = await db.Channels.GetOrInsertAsync(
            c => c.DiscordId == channelDiscordId,
            () => new ChannelEntity
            {
                DiscordId = channelDiscordId,
                GuildId = guildId,
                Name = $"[unknown thread {channelDiscordId}]",
                Type = ChannelType.PublicThread,
                Position = 0,
                IsDeleted = false
            });

        return channel?.Id
            ?? throw new InvalidOperationException(
                $"Placeholder channel {channelDiscordId} vanished after insert-or-get");
    }

    public async Task MarkDeletedAsync(ulong channelDiscordId, DateTime deletedAtUtc)
    {
        await db.Channels
            .Where(c => c.DiscordId == channelDiscordId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsDeleted, true)
                .SetProperty(c => c.DeletedAtUtc, (DateTime?)deletedAtUtc));
    }
}
