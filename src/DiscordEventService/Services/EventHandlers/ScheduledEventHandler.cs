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
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ScheduledGuildEventCreated", e.Guild.Id, e.Channel?.Id, e.Creator?.Id, correlationId: correlationId);

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "ScheduledGuildEventCreated", nameof(ScheduledEventHandler), ex,
                    e.Guild?.Id, e.Channel?.Id, e.Creator?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUpdatedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ScheduledGuildEventUpdated", e.EventAfter.GuildId, e.EventAfter.ChannelId, e.EventAfter.Creator?.Id, correlationId: correlationId);

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "ScheduledGuildEventUpdated", nameof(ScheduledEventHandler), ex,
                    e.EventAfter?.GuildId, e.EventAfter?.ChannelId, e.EventAfter?.Creator?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventDeletedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ScheduledGuildEventDeleted", e.Event.GuildId, e.Event.ChannelId, null, correlationId: correlationId);

                // Mark as deleted
                await db.GuildScheduledEvents
                    .Where(s => s.DiscordId == e.Event.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.IsDeleted, true)
                        .SetProperty(x => x.DeletedAtUtc, (DateTime?)now));

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "ScheduledGuildEventDeleted", nameof(ScheduledEventHandler), ex,
                    e.Event?.GuildId, e.Event?.ChannelId, null, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventCompletedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ScheduledGuildEventCompleted", e.Event.GuildId, e.Event.ChannelId, null, correlationId: correlationId);

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "ScheduledGuildEventCompleted", nameof(ScheduledEventHandler), ex,
                    e.Event?.GuildId, e.Event?.ChannelId, null, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUserAddedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ScheduledGuildEventUserAdded", e.Guild.Id, null, e.User.Id, correlationId: correlationId);

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "ScheduledGuildEventUserAdded", nameof(ScheduledEventHandler), ex,
                    e.Guild?.Id, null, e.User?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ScheduledGuildEventUserRemovedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ScheduledGuildEventUserRemoved", e.Guild.Id, null, e.User.Id, correlationId: correlationId);

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "ScheduledGuildEventUserRemoved", nameof(ScheduledEventHandler), ex,
                    e.Guild?.Id, null, e.User?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    private static async Task UpsertScheduledEventAsync(DiscordDbContext db, DiscordScheduledGuildEvent evt, ulong? creatorDiscordId)
    {
        var guildGuid = await db.Guilds
            .Where(g => g.DiscordId == evt.GuildId)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();
        Guid? channelGuid = evt.ChannelId.HasValue
            ? await db.Channels
                .Where(c => c.DiscordId == evt.ChannelId.Value)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync()
            : null;
        Guid? creatorGuid = creatorDiscordId.HasValue
            ? await db.Users
                .Where(u => u.DiscordId == creatorDiscordId.Value)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync()
            : null;

        var existing = await db.GuildScheduledEvents.FirstOrDefaultAsync(s => s.DiscordId == evt.Id);
        if (existing == null)
        {
            existing = new GuildScheduledEventEntity { DiscordId = evt.Id };
            db.GuildScheduledEvents.Add(existing);
        }

        existing.GuildId = guildGuid;
        existing.ChannelId = channelGuid;
        existing.CreatorId = creatorGuid;
        existing.Name = evt.Name;
        existing.Description = evt.Description;
        existing.Status = (int)evt.Status;
        existing.EntityType = (int)evt.Type;
        existing.ScheduledStartTimeUtc = evt.StartTime.UtcDateTime;
        existing.ScheduledEndTimeUtc = evt.EndTime?.UtcDateTime;
        existing.EntityMetadataLocation = evt.Metadata?.Location;
        existing.IsDeleted = false;
        existing.DeletedAtUtc = null;
    }
}
