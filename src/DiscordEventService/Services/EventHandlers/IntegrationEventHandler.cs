using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.EventHandlers;

public class IntegrationEventHandler(IServiceScopeFactory scopeFactory, ILogger<IntegrationEventHandler> logger) :
    IEventHandler<IntegrationCreatedEventArgs>,
    IEventHandler<IntegrationUpdatedEventArgs>,
    IEventHandler<IntegrationDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, IntegrationCreatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "IntegrationCreated", e.Guild.Id, null, null);

            // Look up Guid FK
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);

            db.Integrations.Add(new IntegrationEntity
            {
                DiscordId = e.Integration.Id,
                GuildId = guild?.Id ?? Guid.Empty,
                Name = e.Integration.Name,
                Type = e.Integration.Type,
                IsEnabled = e.Integration.IsEnabled
            });

            db.IntegrationEvents.Add(new IntegrationEventEntity
            {
                IntegrationDiscordId = e.Integration.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = IntegrationEventType.Created,
                Name = e.Integration.Name,
                Type = e.Integration.Type,
                IsEnabled = e.Integration.IsEnabled,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling integration created");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, IntegrationUpdatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "IntegrationUpdated", e.Guild.Id, null, null);

            await db.Integrations
                .Where(i => i.DiscordId == e.Integration.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Name, e.Integration.Name)
                    .SetProperty(i => i.IsEnabled, e.Integration.IsEnabled));

            db.IntegrationEvents.Add(new IntegrationEventEntity
            {
                IntegrationDiscordId = e.Integration.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = IntegrationEventType.Updated,
                Name = e.Integration.Name,
                Type = e.Integration.Type,
                IsEnabled = e.Integration.IsEnabled,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling integration updated");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, IntegrationDeletedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "IntegrationDeleted", e.Guild.Id, null, null);

            await db.Integrations
                .Where(i => i.DiscordId == e.IntegrationId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsDeleted, true));

            db.IntegrationEvents.Add(new IntegrationEventEntity
            {
                IntegrationDiscordId = e.IntegrationId,
                GuildDiscordId = e.Guild.Id,
                EventType = IntegrationEventType.Deleted,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling integration deleted");
        }
    }
}
