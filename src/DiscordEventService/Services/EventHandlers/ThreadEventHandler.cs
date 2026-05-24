using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.Entities;
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
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();
                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ThreadCreated", e.Guild.Id, e.Thread.Id, e.Thread.CreatorId, correlationId: correlationId);

                // Flush the raw event row before any upsert; the upsert services' 23505 catch path
                // calls ChangeTracker.Clear() which would otherwise drop the staged raw_event_logs row.
                await db.SaveChangesAsync();

                // Threads are channels (ADR-0003): upsert the thread into `channels` so messages
                // sent into it have a non-null channel_id from the first event onward.
                var guildId = await guildUpsert.UpsertGuildAsync(e.Guild);
                await channelUpsert.UpsertChannelAsync(e.Thread, guildId);

                // For message threads (parent is text/news), thread ID == starter message ID
                var isMessageThread = e.Parent?.Type is DiscordChannelType.Text or DiscordChannelType.News;

                var threadEvent = new ThreadEventEntity
                {
                    ThreadDiscordId = e.Thread.Id,
                    ParentChannelDiscordId = e.Thread.ParentId ?? 0,
                    GuildDiscordId = e.Guild.Id,
                    EventType = ThreadEventType.Created,
                    Name = e.Thread.Name,
                    OwnerDiscordId = e.Thread.CreatorId,
                    StarterMessageDiscordId = isMessageThread ? e.Thread.Id : null,
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
                    e.Guild?.Id, e.Thread?.Id, e.Thread?.CreatorId, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadUpdatedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();
                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ThreadUpdated", e.Guild.Id, e.ThreadAfter.Id, e.ThreadAfter.CreatorId, correlationId: correlationId);

                await db.SaveChangesAsync();

                var guildId = await guildUpsert.UpsertGuildAsync(e.Guild);
                await channelUpsert.UpsertChannelAsync(e.ThreadAfter, guildId);

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
                    e.Guild?.Id, e.ThreadAfter?.Id, e.ThreadAfter?.CreatorId, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadDeletedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();
                var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ThreadDeleted", e.Guild.Id, e.Thread.Id, e.Thread.CreatorId, correlationId: correlationId);

                await db.SaveChangesAsync();

                await channelUpsert.MarkDeletedAsync(e.Thread.Id, DateTime.UtcNow);

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
                    e.Guild?.Id, e.Thread?.Id, e.Thread?.CreatorId, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, ThreadMembersUpdatedEventArgs e)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "ThreadMembersUpdated", e.Guild.Id, e.Thread.Id, null, correlationId: correlationId);

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
                    e.Guild?.Id, e.Thread?.Id, null, rawJson, correlationId: correlationId);
            }
        }
    }
}
