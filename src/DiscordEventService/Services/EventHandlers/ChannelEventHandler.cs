using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class ChannelEventHandler(IServiceScopeFactory scopeFactory, ILogger<ChannelEventHandler> logger) :
    IEventHandler<ChannelCreatedEventArgs>,
    IEventHandler<ChannelUpdatedEventArgs>,
    IEventHandler<ChannelDeletedEventArgs>,
    IEventHandler<ChannelPinsUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ChannelCreatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ChannelCreated", e.Guild.Id, e.Channel.Id, null);

            // Look up Guild Guid
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);

            var channelEntity = await db.Channels
                .FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);

            if (channelEntity == null)
            {
                channelEntity = new ChannelEntity
                {
                    DiscordId = e.Channel.Id,
                    GuildId = guild?.Id ?? Guid.Empty
                };
                db.Channels.Add(channelEntity);
            }

            UpdateChannelEntity(channelEntity, e.Channel);

            var channelEvent = new ChannelEventEntity
            {
                ChannelDiscordId = e.Channel.Id,
                GuildDiscordId = e.Guild.Id,
                ChannelType = (int)e.Channel.Type,
                EventType = ChannelEventType.Created,
                NameAfter = e.Channel.Name,
                TopicAfter = e.Channel.Topic,
                PositionAfter = e.Channel.Position,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ChannelEvents.Add(channelEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling channel created for ChannelId={ChannelId}", e.Channel.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelUpdatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ChannelUpdated", e.ChannelAfter.Guild.Id, e.ChannelAfter.Id, null);

            var channelEntity = await db.Channels
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
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
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

            db.ChannelEvents.Add(channelEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling channel updated for ChannelId={ChannelId}", e.ChannelAfter.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelDeletedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ChannelDeleted", e.Guild.Id, e.Channel.Id, null);

            var channelEntity = await db.Channels
                .FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);

            if (channelEntity != null)
            {
                channelEntity.IsDeleted = true;
            }

            var channelEvent = new ChannelEventEntity
            {
                ChannelDiscordId = e.Channel.Id,
                GuildDiscordId = e.Guild.Id,
                ChannelType = (int)e.Channel.Type,
                EventType = ChannelEventType.Deleted,
                NameBefore = e.Channel.Name,
                TopicBefore = e.Channel.Topic,
                PositionBefore = e.Channel.Position,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ChannelEvents.Add(channelEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling channel deleted for ChannelId={ChannelId}", e.Channel.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ChannelPinsUpdatedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ChannelPinsUpdatedChannel", e.Guild?.Id ?? 0, e.Channel.Id, null);

            var channelEvent = new ChannelEventEntity
            {
                ChannelDiscordId = e.Channel.Id,
                GuildDiscordId = e.Guild?.Id ?? 0,
                ChannelType = (int)e.Channel.Type,
                EventType = ChannelEventType.PinsUpdated,
                EventTimestampUtc = e.LastPinTimestamp?.UtcDateTime ?? DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ChannelEvents.Add(channelEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling channel pins updated for ChannelId={ChannelId}", e.Channel.Id);
        }
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
    }
}
