using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class StageInstanceEventHandler(IServiceScopeFactory scopeFactory, ILogger<StageInstanceEventHandler> logger) :
    IEventHandler<StageInstanceCreatedEventArgs>,
    IEventHandler<StageInstanceUpdatedEventArgs>,
    IEventHandler<StageInstanceDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, StageInstanceCreatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "StageInstanceCreated", e.StageInstance.GuildId, e.StageInstance.ChannelId, null);

            // Look up Guid FKs
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.StageInstance.GuildId);
            var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == e.StageInstance.ChannelId);

            db.StageInstances.Add(new StageInstanceEntity
            {
                DiscordId = e.StageInstance.Id,
                GuildId = guild?.Id ?? Guid.Empty,
                ChannelId = channel?.Id ?? Guid.Empty,
                Topic = e.StageInstance.Topic,
                PrivacyLevel = (int)e.StageInstance.PrivacyLevel
            });

            db.StageInstanceEvents.Add(new StageInstanceEventEntity
            {
                StageInstanceDiscordId = e.StageInstance.Id,
                GuildDiscordId = e.StageInstance.GuildId,
                ChannelDiscordId = e.StageInstance.ChannelId,
                EventType = StageInstanceEventType.Created,
                TopicAfter = e.StageInstance.Topic,
                PrivacyLevelAfter = (int)e.StageInstance.PrivacyLevel,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling stage instance created");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, StageInstanceUpdatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "StageInstanceUpdated", e.StageInstanceAfter.GuildId, e.StageInstanceAfter.ChannelId, null);

            await db.StageInstances
                .Where(s => s.DiscordId == e.StageInstanceAfter.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(st => st.Topic, e.StageInstanceAfter.Topic)
                    .SetProperty(st => st.PrivacyLevel, (int)e.StageInstanceAfter.PrivacyLevel));

            db.StageInstanceEvents.Add(new StageInstanceEventEntity
            {
                StageInstanceDiscordId = e.StageInstanceAfter.Id,
                GuildDiscordId = e.StageInstanceAfter.GuildId,
                ChannelDiscordId = e.StageInstanceAfter.ChannelId,
                EventType = StageInstanceEventType.Updated,
                TopicBefore = e.StageInstanceBefore?.Topic,
                TopicAfter = e.StageInstanceAfter.Topic,
                PrivacyLevelBefore = e.StageInstanceBefore != null ? (int)e.StageInstanceBefore.PrivacyLevel : null,
                PrivacyLevelAfter = (int)e.StageInstanceAfter.PrivacyLevel,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling stage instance updated");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, StageInstanceDeletedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "StageInstanceDeleted", e.StageInstance.GuildId, e.StageInstance.ChannelId, null);

            await db.StageInstances
                .Where(s => s.DiscordId == e.StageInstance.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(st => st.IsDeleted, true));

            db.StageInstanceEvents.Add(new StageInstanceEventEntity
            {
                StageInstanceDiscordId = e.StageInstance.Id,
                GuildDiscordId = e.StageInstance.GuildId,
                ChannelDiscordId = e.StageInstance.ChannelId,
                EventType = StageInstanceEventType.Deleted,
                TopicBefore = e.StageInstance.Topic,
                PrivacyLevelBefore = (int)e.StageInstance.PrivacyLevel,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling stage instance deleted");
        }
    }
}
