using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class ChannelEventHandler(EventPipeline pipeline) :
    IEventHandler<ChannelCreatedEventArgs>,
    IEventHandler<ChannelUpdatedEventArgs>,
    IEventHandler<ChannelDeletedEventArgs>,
    IEventHandler<ChannelPinsUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ChannelCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelCreated", nameof(ChannelEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var guildGuid = (await guildUpsert.UpsertGuildAsync(e.Guild)).Value;

                // Upsert the channel (handles the 23505 race internally) before staging the event,
                // then commit channel + event together. ExecutionStrategy is required because
                // EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // Reset the tracker so an EnableRetryOnFailure retry doesn't re-stage entities
                    // left Added by a rolled-back attempt.
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    await UpsertCreatedChannelAsync(ctx, e.Channel, guildGuid);

                    ctx.Db.ChannelEvents.Add(BuildChannelCreatedEvent(e, ctx));
                    await ctx.Db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelUpdated", nameof(ChannelEventHandler),
            e.ChannelAfter.Guild.Id, e.ChannelAfter.Id, null, async ctx =>
            {
                var channelEntity = await ctx.Db.Channels
                    .FirstOrDefaultAsync(c => c.DiscordId == e.ChannelAfter.Id);

                if (channelEntity is not null)
                    UpdateChannelEntity(channelEntity, e.ChannelAfter);

                var channelEvent = new ChannelEventEntity
                {
                    ChannelDiscordId = e.ChannelAfter.Id,
                    GuildDiscordId = e.ChannelAfter.Guild.Id,
                    ChannelType = (int)e.ChannelAfter.Type,
                    EventType = ChannelEventType.Updated,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                };

                if (e.ChannelBefore.Name != e.ChannelAfter.Name)
                {
                    channelEvent.NameBefore = e.ChannelBefore.Name;
                    channelEvent.NameAfter = e.ChannelAfter.Name;
                }

                if (e.ChannelBefore.Topic != e.ChannelAfter.Topic)
                {
                    channelEvent.TopicBefore = e.ChannelBefore.Topic;
                    channelEvent.TopicAfter = e.ChannelAfter.Topic;
                }

                if (e.ChannelBefore.Position != e.ChannelAfter.Position)
                {
                    channelEvent.PositionBefore = e.ChannelBefore.Position;
                    channelEvent.PositionAfter = e.ChannelAfter.Position;
                }

                ctx.Db.ChannelEvents.Add(channelEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelDeletedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelDeleted", nameof(ChannelEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var channelEntity = await ctx.Db.Channels
                    .FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);

                if (channelEntity is not null)
                {
                    channelEntity.IsDeleted = true;
                    channelEntity.DeletedAtUtc = ctx.ReceivedAtUtc;
                }

                ctx.Db.ChannelEvents.Add(new ChannelEventEntity
                {
                    ChannelDiscordId = e.Channel.Id,
                    GuildDiscordId = e.Guild.Id,
                    ChannelType = (int)e.Channel.Type,
                    EventType = ChannelEventType.Deleted,
                    NameBefore = e.Channel.Name,
                    TopicBefore = e.Channel.Topic,
                    PositionBefore = e.Channel.Position,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelPinsUpdatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "ChannelPinsUpdatedChannel", nameof(ChannelEventHandler),
            e.Guild?.Id ?? 0, e.Channel.Id, null, async ctx =>
            {
                ctx.Db.ChannelEvents.Add(new ChannelEventEntity
                {
                    ChannelDiscordId = e.Channel.Id,
                    GuildDiscordId = e.Guild?.Id ?? 0,
                    ChannelType = (int)e.Channel.Type,
                    EventType = ChannelEventType.PinsUpdated,
                    EventTimestampUtc = e.LastPinTimestamp?.UtcDateTime ?? ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    private static void UpdateChannelEntity(ChannelEntity entity, DiscordChannel channel)
    {
        entity.Name = channel.Name;
        entity.Type = (ChannelType)channel.Type;
        entity.Topic = channel.Topic;
        entity.Position = channel.Position;
        entity.ParentDiscordId = channel.ParentId;
        entity.Bitrate = channel.Bitrate;
        entity.UserLimit = channel.UserLimit;
        entity.RateLimitPerUser = channel.PerUserRateLimit;
        entity.IsNsfw = channel.IsNSFW;
        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;
    }

    private static async Task UpsertCreatedChannelAsync(EventContext ctx, DiscordChannel channel, Guid guildGuid)
    {
        await ctx.Db.Channels.UpsertAsync(
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
                .SetProperty(c => c.IsNsfw, channel.IsNSFW)
                .SetProperty(c => c.IsDeleted, false)
                .SetProperty(c => c.DeletedAtUtc, (DateTime?)null),
            () => new ChannelEntity
            {
                DiscordId = channel.Id,
                GuildId = guildGuid,
                Name = channel.Name,
                Type = (ChannelType)channel.Type,
                Topic = channel.Topic,
                Position = channel.Position,
                ParentDiscordId = channel.ParentId,
                Bitrate = channel.Bitrate,
                UserLimit = channel.UserLimit,
                RateLimitPerUser = channel.PerUserRateLimit,
                IsNsfw = channel.IsNSFW,
                IsDeleted = false,
            },
            c => c.Id);
    }

    private static ChannelEventEntity BuildChannelCreatedEvent(ChannelCreatedEventArgs e, EventContext ctx) => new ChannelEventEntity
    {
        ChannelDiscordId = e.Channel.Id,
        GuildDiscordId = e.Guild.Id,
        ChannelType = (int)e.Channel.Type,
        EventType = ChannelEventType.Created,
        NameAfter = e.Channel.Name,
        TopicAfter = e.Channel.Topic,
        PositionAfter = e.Channel.Position,
        EventTimestampUtc = ctx.ReceivedAtUtc,
        ReceivedAtUtc = ctx.ReceivedAtUtc,
        RawEventJson = ctx.RawJson,
    };
}
