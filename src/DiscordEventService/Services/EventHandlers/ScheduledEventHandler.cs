using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class ScheduledEventHandler(IServiceScopeFactory scopeFactory, ILogger<ScheduledEventHandler> logger) :
    IEventHandler<ScheduledGuildEventCreatedEventArgs>,
    IEventHandler<ScheduledGuildEventUpdatedEventArgs>,
    IEventHandler<ScheduledGuildEventDeletedEventArgs>,
    IEventHandler<ScheduledGuildEventCompletedEventArgs>,
    IEventHandler<ScheduledGuildEventUserAddedEventArgs>,
    IEventHandler<ScheduledGuildEventUserRemovedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventCreatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ScheduledGuildEventCreated", e.Guild.Id, e.Channel?.Id, e.Creator?.Id);

            await UpsertScheduledEventAsync(db, e.Event, e.Creator?.Id);

            var entity = new ScheduledEventEntity
            {
                EventDiscordId = e.Event.Id,
                GuildDiscordId = e.Guild.Id,
                ChannelDiscordId = e.Channel?.Id,
                CreatorDiscordId = e.Creator?.Id,
                EventType = ScheduledEventEventType.Created,
                Name = e.Event.Name,
                Description = e.Event.Description,
                Status = (int)e.Event.Status,
                EntityType = (int)e.Event.Type,
                ScheduledStartTime = e.Event.StartTime.UtcDateTime,
                ScheduledEndTime = e.Event.EndTime?.UtcDateTime,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.ScheduledEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling scheduled event created");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUpdatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ScheduledGuildEventUpdated", e.EventAfter.GuildId, e.EventAfter.ChannelId, e.EventAfter.Creator?.Id);

            await UpsertScheduledEventAsync(db, e.EventAfter, e.EventAfter.Creator?.Id);

            var entity = new ScheduledEventEntity
            {
                EventDiscordId = e.EventAfter.Id,
                GuildDiscordId = e.EventAfter.GuildId,
                ChannelDiscordId = e.EventAfter.ChannelId,
                CreatorDiscordId = e.EventAfter.Creator?.Id,
                EventType = ScheduledEventEventType.Updated,
                Name = e.EventAfter.Name,
                Description = e.EventAfter.Description,
                Status = (int)e.EventAfter.Status,
                EntityType = (int)e.EventAfter.Type,
                ScheduledStartTime = e.EventAfter.StartTime.UtcDateTime,
                ScheduledEndTime = e.EventAfter.EndTime?.UtcDateTime,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.ScheduledEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling scheduled event updated");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventDeletedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ScheduledGuildEventDeleted", e.Event.GuildId, e.Event.ChannelId, null);

            // Mark as deleted
            await db.GuildScheduledEvents
                .Where(s => s.DiscordId == e.Event.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));

            var entity = new ScheduledEventEntity
            {
                EventDiscordId = e.Event.Id,
                GuildDiscordId = e.Event.GuildId,
                ChannelDiscordId = e.Event.ChannelId,
                EventType = ScheduledEventEventType.Deleted,
                Name = e.Event.Name,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.ScheduledEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling scheduled event deleted");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventCompletedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ScheduledGuildEventCompleted", e.Event.GuildId, e.Event.ChannelId, null);

            await UpsertScheduledEventAsync(db, e.Event, e.Event.Creator?.Id);

            var entity = new ScheduledEventEntity
            {
                EventDiscordId = e.Event.Id,
                GuildDiscordId = e.Event.GuildId,
                ChannelDiscordId = e.Event.ChannelId,
                EventType = ScheduledEventEventType.Completed,
                Name = e.Event.Name,
                Status = (int)e.Event.Status,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.ScheduledEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling scheduled event completed");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUserAddedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ScheduledGuildEventUserAdded", e.Guild.Id, null, e.User.Id);

            // Increment user count
            await db.GuildScheduledEvents
                .Where(s => s.DiscordId == e.Event.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserCount, x => x.UserCount + 1));

            var entity = new ScheduledEventEntity
            {
                EventDiscordId = e.Event.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = ScheduledEventEventType.UserAdded,
                UserDiscordId = e.User.Id,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.ScheduledEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling scheduled event user added");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUserRemovedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ScheduledGuildEventUserRemoved", e.Guild.Id, null, e.User.Id);

            // Decrement user count
            await db.GuildScheduledEvents
                .Where(s => s.DiscordId == e.Event.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserCount, x => x.UserCount - 1));

            var entity = new ScheduledEventEntity
            {
                EventDiscordId = e.Event.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = ScheduledEventEventType.UserRemoved,
                UserDiscordId = e.User.Id,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.ScheduledEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling scheduled event user removed");
        }
    }

    private static async Task UpsertScheduledEventAsync(DiscordDbContext db, DiscordScheduledGuildEvent evt, ulong? creatorDiscordId)
    {
        // Look up Guid FKs
        var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == evt.GuildId);
        var channel = evt.ChannelId.HasValue
            ? await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == evt.ChannelId.Value)
            : null;
        var creator = creatorDiscordId.HasValue
            ? await db.Users.FirstOrDefaultAsync(u => u.DiscordId == creatorDiscordId.Value)
            : null;

        var existing = await db.GuildScheduledEvents.FirstOrDefaultAsync(s => s.DiscordId == evt.Id);
        if (existing == null)
        {
            existing = new GuildScheduledEventEntity { DiscordId = evt.Id };
            db.GuildScheduledEvents.Add(existing);
        }

        existing.GuildId = guild?.Id ?? Guid.Empty;
        existing.ChannelId = channel?.Id;
        existing.CreatorId = creator?.Id;
        existing.Name = evt.Name;
        existing.Description = evt.Description;
        existing.Status = (int)evt.Status;
        existing.EntityType = (int)evt.Type;
        existing.ScheduledStartTimeUtc = evt.StartTime.UtcDateTime;
        existing.ScheduledEndTimeUtc = evt.EndTime?.UtcDateTime;
        existing.EntityMetadataLocation = evt.Metadata?.Location;
        existing.IsDeleted = false;
    }
}
