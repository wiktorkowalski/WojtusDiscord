using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

// The single write seam for channel rows (#290): live events, guild cold-sync, and
// backfill all go through UpsertChannelAsync so the column map exists exactly once.
internal sealed class ChannelUpsertService(DiscordDbContext db, ILogger<ChannelUpsertService> logger)
{
    public async Task<UpsertResult<Guid>> UpsertChannelAsync(
        DiscordChannel channel, Guid guildId, CancellationToken cancellationToken = default)
    {
        // Raw-int cast, not a switch: unmodeled Discord types must keep their value in the DB
        // (Unknown=-1 is only a routing label — see ChannelType). The warning keeps drift visible.
        var type = (ChannelType)channel.Type;
        if (!Enum.IsDefined(type))
        {
            logger.LogWarning(
                "Unknown Discord channel type {ChannelTypeValue} for channel {ChannelId}; persisting raw value",
                (int)type, channel.Id);
        }

        // A live sighting means the channel exists — always clear soft-deletion.
        var id = await db.Channels.UpsertAsync(
            c => c.DiscordId == channel.Id,
            s => s
                .SetProperty(c => c.Name, channel.Name)
                .SetProperty(c => c.Type, type)
                .SetProperty(c => c.Topic, channel.Topic)
                .SetProperty(c => c.Position, channel.Position)
                .SetProperty(c => c.ParentDiscordId, channel.ParentId)
                .SetProperty(c => c.Bitrate, channel.Bitrate)
                .SetProperty(c => c.UserLimit, channel.UserLimit)
                .SetProperty(c => c.RateLimitPerUser, channel.PerUserRateLimit)
                .SetProperty(c => c.IsNsfw, channel.IsNSFW)
                .SetProperty(c => c.IsDeleted, false)
                .SetProperty(c => c.DeletedAtUtc, (DateTime?)null),
            () => new ChannelEntity
            {
                DiscordId = channel.Id,
                GuildId = guildId,
                Name = channel.Name,
                Type = type,
                Topic = channel.Topic,
                Position = channel.Position,
                ParentDiscordId = channel.ParentId,
                Bitrate = channel.Bitrate,
                UserLimit = channel.UserLimit,
                RateLimitPerUser = channel.PerUserRateLimit,
                IsNsfw = channel.IsNSFW,
                IsDeleted = false
            },
            c => c.Id,
            cancellationToken);

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
