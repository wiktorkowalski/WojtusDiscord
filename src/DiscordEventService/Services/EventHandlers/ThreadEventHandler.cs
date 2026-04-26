using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class ThreadEventHandler(IServiceScopeFactory scopeFactory, ILogger<ThreadEventHandler> logger) :
    IEventHandler<ThreadCreatedEventArgs>,
    IEventHandler<ThreadUpdatedEventArgs>,
    IEventHandler<ThreadDeletedEventArgs>,
    IEventHandler<ThreadMembersUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ThreadCreatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ThreadCreated", e.Guild.Id, e.Thread.Id, e.Thread.CreatorId);

            var threadEvent = new ThreadEventEntity
            {
                ThreadDiscordId = e.Thread.Id,
                ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                GuildDiscordId = e.Guild.Id,
                EventType = ThreadEventType.Created,
                Name = e.Thread.Name,
                OwnerDiscordId = e.Thread.CreatorId,
                IsArchived = e.Thread.ThreadMetadata?.IsArchived ?? false,
                IsLocked = e.Thread.ThreadMetadata?.IsLocked ?? false,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ThreadEvents.Add(threadEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling thread created for ThreadId={ThreadId}", e.Thread.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "ThreadCreated", nameof(ThreadEventHandler), ex,
                e.Guild?.Id, e.Thread?.Id, e.Thread?.CreatorId, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadUpdatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ThreadUpdated", e.Guild.Id, e.ThreadAfter.Id, e.ThreadAfter.CreatorId);

            var threadEvent = new ThreadEventEntity
            {
                ThreadDiscordId = e.ThreadAfter.Id,
                ParentChannelDiscordId = e.ThreadAfter.ParentId ?? 0,
                GuildDiscordId = e.Guild.Id,
                EventType = ThreadEventType.Updated,
                Name = e.ThreadAfter.Name,
                OwnerDiscordId = e.ThreadAfter.CreatorId,
                IsArchived = e.ThreadAfter.ThreadMetadata?.IsArchived ?? false,
                IsLocked = e.ThreadAfter.ThreadMetadata?.IsLocked ?? false,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ThreadEvents.Add(threadEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling thread updated for ThreadId={ThreadId}", e.ThreadAfter.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "ThreadUpdated", nameof(ThreadEventHandler), ex,
                e.Guild?.Id, e.ThreadAfter?.Id, e.ThreadAfter?.CreatorId, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadDeletedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ThreadDeleted", e.Guild.Id, e.Thread.Id, e.Thread.CreatorId);

            var threadEvent = new ThreadEventEntity
            {
                ThreadDiscordId = e.Thread.Id,
                ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                GuildDiscordId = e.Guild.Id,
                EventType = ThreadEventType.Deleted,
                Name = e.Thread.Name,
                OwnerDiscordId = e.Thread.CreatorId,
                IsArchived = e.Thread.ThreadMetadata?.IsArchived ?? false,
                IsLocked = e.Thread.ThreadMetadata?.IsLocked ?? false,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ThreadEvents.Add(threadEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling thread deleted for ThreadId={ThreadId}", e.Thread.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "ThreadDeleted", nameof(ThreadEventHandler), ex,
                e.Guild?.Id, e.Thread?.Id, e.Thread?.CreatorId, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadMembersUpdatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ThreadMembersUpdated", e.Guild.Id, e.Thread.Id, null);

            string? membersAddedJson = null;
            string? membersRemovedJson = null;

            if (e.AddedMembers.Count > 0)
            {
                var addedMemberIds = e.AddedMembers.Select(m => m.Id).ToList();
                membersAddedJson = JsonSerializer.Serialize(addedMemberIds);
            }

            if (e.RemovedMembers.Count > 0)
            {
                var removedMemberIds = e.RemovedMembers.Select(m => m.Id).ToList();
                membersRemovedJson = JsonSerializer.Serialize(removedMemberIds);
            }

            var threadEvent = new ThreadEventEntity
            {
                ThreadDiscordId = e.Thread.Id,
                ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                GuildDiscordId = e.Guild.Id,
                EventType = ThreadEventType.MembersUpdated,
                Name = e.Thread.Name,
                OwnerDiscordId = e.Thread.CreatorId,
                IsArchived = e.Thread.ThreadMetadata?.IsArchived ?? false,
                IsLocked = e.Thread.ThreadMetadata?.IsLocked ?? false,
                MembersAddedJson = membersAddedJson,
                MembersRemovedJson = membersRemovedJson,
                EventTimestampUtc = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.ThreadEvents.Add(threadEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling thread members updated for ThreadId={ThreadId}", e.Thread.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "ThreadMembersUpdated", nameof(ThreadEventHandler), ex,
                e.Guild?.Id, e.Thread?.Id, null, rawJson);
        }
    }
}
