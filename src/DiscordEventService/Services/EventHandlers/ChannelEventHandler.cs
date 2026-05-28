using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public sealed class ChannelEventHandler(EventPipeline pipeline) :
    IEventHandler<ChannelCreatedEventArgs>,
    IEventHandler<ChannelUpdatedEventArgs>,
    IEventHandler<ChannelDeletedEventArgs>,
    IEventHandler<ChannelPinsUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ChannelCreatedEventArgs e)
    {
        await pipeline.Execute(e, "ChannelCreated", nameof(ChannelEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);

                ChannelEventEntity NewChannelEvent() => new()
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
                    RawEventJson = ctx.RawJson
                };

                var channelEntity = await ctx.Db.Channels
                    .FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);

                if (channelEntity == null)
                {
                    channelEntity = new ChannelEntity
                    {
                        DiscordId = e.Channel.Id,
                        GuildId = guildGuid
                    };
                    ctx.Db.Channels.Add(channelEntity);
                }

                UpdateChannelEntity(channelEntity, e.Channel);
                ctx.Db.ChannelEvents.Add(NewChannelEvent());

                try
                {
                    await ctx.Db.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    // Concurrent insert won the race. Re-fetch the now-existing row, update it, re-add the event.
                    ctx.Db.ChangeTracker.Clear();
                    var existing = await ctx.Db.Channels.FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);
                    if (existing != null)
                    {
                        UpdateChannelEntity(existing, e.Channel);
                    }
                    ctx.Db.ChannelEvents.Add(NewChannelEvent());
                    await ctx.Db.SaveChangesAsync();
                }
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "ChannelUpdated", nameof(ChannelEventHandler),
            e.ChannelAfter.Guild.Id, e.ChannelAfter.Id, null, async ctx =>
            {
                var channelEntity = await ctx.Db.Channels
                    .FirstOrDefaultAsync(c => c.DiscordId == e.ChannelAfter.Id);

                if (channelEntity != null)
                {
                    UpdateChannelEntity(channelEntity, e.ChannelAfter);
                }

                var channelEvent = new ChannelEventEntity
                {
                    ChannelDiscordId = e.ChannelAfter.Id,
                    GuildDiscordId = e.ChannelAfter.Guild.Id,
                    ChannelType = (int)e.ChannelAfter.Type,
                    EventType = ChannelEventType.Updated,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
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
        await pipeline.Execute(e, "ChannelDeleted", nameof(ChannelEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var channelEntity = await ctx.Db.Channels
                    .FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);

                if (channelEntity != null)
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
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelPinsUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "ChannelPinsUpdatedChannel", nameof(ChannelEventHandler),
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
                    RawEventJson = ctx.RawJson
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
}
