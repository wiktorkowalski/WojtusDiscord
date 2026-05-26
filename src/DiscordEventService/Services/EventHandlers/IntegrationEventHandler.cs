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
                    e, "IntegrationCreated", e.Guild.Id, null, null, correlationId: correlationId);

                await db.SaveChangesAsync();

                var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                var guildGuid = await guildUpsert.UpsertGuildAsync(e.Guild);

                db.Integrations.Add(new IntegrationEntity
                {
                    DiscordId = e.Integration.Id,
                    GuildId = guildGuid,
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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "IntegrationCreated", nameof(IntegrationEventHandler), ex,
                    e.Guild?.Id, null, null, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, IntegrationUpdatedEventArgs e)
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
                    e, "IntegrationUpdated", e.Guild.Id, null, null, correlationId: correlationId);

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "IntegrationUpdated", nameof(IntegrationEventHandler), ex,
                    e.Guild?.Id, null, null, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, IntegrationDeletedEventArgs e)
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
                    e, "IntegrationDeleted", e.Guild.Id, null, null, correlationId: correlationId);

                await db.Integrations
                    .Where(i => i.DiscordId == e.IntegrationId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.IsDeleted, true)
                        .SetProperty(i => i.DeletedAtUtc, (DateTime?)now));

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
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "IntegrationDeleted", nameof(IntegrationEventHandler), ex,
                    e.Guild?.Id, null, null, rawJson, correlationId: correlationId);
            }
        }
    }
}
