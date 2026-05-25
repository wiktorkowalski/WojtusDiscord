using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class AutoModRuleEventHandler(IServiceScopeFactory scopeFactory, ILogger<AutoModRuleEventHandler> logger) :
    IEventHandler<AutoModerationRuleCreatedEventArgs>,
    IEventHandler<AutoModerationRuleUpdatedEventArgs>,
    IEventHandler<AutoModerationRuleDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleCreatedEventArgs e)
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
                var userService = scope.ServiceProvider.GetRequiredService<UserService>();

                var guildDiscordId = e.Rule.Guild?.Id ?? 0UL;
                var creatorDiscordId = e.Rule.Creator?.Id ?? 0UL;

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "AutoModRuleCreatedRule", guildDiscordId, null, creatorDiscordId, correlationId: correlationId);

                await db.SaveChangesAsync();

                Guid? guildGuid = null;
                if (e.Rule.Guild != null)
                {
                    var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
                    var id = await guildUpsert.UpsertGuildAsync(e.Rule.Guild);
                    guildGuid = id != Guid.Empty ? id : null;
                }

                Guid? creatorGuid = null;
                if (e.Rule.Creator != null)
                {
                    await userService.UpsertUserAsync(e.Rule.Creator);
                    creatorGuid = await db.Users
                        .Where(u => u.DiscordId == e.Rule.Creator.Id)
                        .Select(u => (Guid?)u.Id)
                        .FirstOrDefaultAsync();
                }

                db.AutoModRules.Add(new AutoModRuleEntity
                {
                    DiscordId = e.Rule.Id,
                    GuildId = guildGuid,
                    CreatorId = creatorGuid,
                    Name = e.Rule.Name ?? string.Empty,
                    EventType = (int)e.Rule.EventType,
                    TriggerType = (int)e.Rule.TriggerType,
                    IsEnabled = e.Rule.IsEnabled,
                    ActionsJson = e.Rule.Actions != null ? JsonSerializer.Serialize(e.Rule.Actions) : null
                });

                db.AutoModRuleEvents.Add(new AutoModRuleEventEntity
                {
                    RuleDiscordId = e.Rule.Id,
                    GuildDiscordId = guildDiscordId,
                    CreatorDiscordId = creatorDiscordId,
                    EventType = AutoModRuleEventType.Created,
                    Name = e.Rule.Name ?? string.Empty,
                    TriggerType = (int)e.Rule.TriggerType,
                    IsEnabled = e.Rule.IsEnabled,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now,
                    RawEventJson = rawJson
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling automod rule created");
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "AutoModRuleCreated", nameof(AutoModRuleEventHandler), ex,
                    e.Rule.Guild?.Id, null, e.Rule.Creator?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleUpdatedEventArgs e)
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

                var guildDiscordId = e.Rule.Guild?.Id ?? 0UL;

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "AutoModRuleUpdatedRule", guildDiscordId, null, e.Rule.Creator?.Id, correlationId: correlationId);

                await db.SaveChangesAsync();

                await db.AutoModRules
                    .Where(r => r.DiscordId == e.Rule.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Name, e.Rule.Name ?? string.Empty)
                        .SetProperty(r => r.IsEnabled, e.Rule.IsEnabled)
                        .SetProperty(r => r.ActionsJson, e.Rule.Actions != null ? JsonSerializer.Serialize(e.Rule.Actions) : null));

                db.AutoModRuleEvents.Add(new AutoModRuleEventEntity
                {
                    RuleDiscordId = e.Rule.Id,
                    GuildDiscordId = guildDiscordId,
                    CreatorDiscordId = e.Rule.Creator?.Id ?? 0,
                    EventType = AutoModRuleEventType.Updated,
                    Name = e.Rule.Name ?? string.Empty,
                    TriggerType = (int)e.Rule.TriggerType,
                    IsEnabled = e.Rule.IsEnabled,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now,
                    RawEventJson = rawJson
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling automod rule updated");
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "AutoModRuleUpdated", nameof(AutoModRuleEventHandler), ex,
                    e.Rule.Guild?.Id, null, e.Rule.Creator?.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleDeletedEventArgs e)
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

                var guildDiscordId = e.Rule.Guild?.Id ?? 0;

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "AutoModRuleDeletedRule", guildDiscordId, null, e.Rule.Creator?.Id, correlationId: correlationId);

                await db.SaveChangesAsync();

                await db.AutoModRules
                    .Where(r => r.DiscordId == e.Rule.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.IsDeleted, true)
                        .SetProperty(r => r.DeletedAtUtc, (DateTime?)now));

                db.AutoModRuleEvents.Add(new AutoModRuleEventEntity
                {
                    RuleDiscordId = e.Rule.Id,
                    GuildDiscordId = guildDiscordId,
                    CreatorDiscordId = e.Rule.Creator?.Id ?? 0,
                    EventType = AutoModRuleEventType.Deleted,
                    Name = e.Rule.Name ?? string.Empty,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now,
                    RawEventJson = rawJson
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling automod rule deleted");
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "AutoModRuleDeleted", nameof(AutoModRuleEventHandler), ex,
                    e.Rule.Guild?.Id, null, e.Rule.Creator?.Id, rawJson, correlationId: correlationId);
            }
        }
    }
}
