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
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var guildDiscordId = e.Rule.Guild?.Id ?? 0;
            var creatorDiscordId = e.Rule.Creator?.Id ?? 0;

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModRuleCreatedRule", guildDiscordId, null, creatorDiscordId);

            // Look up Guid FKs
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildDiscordId);
            var creator = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == creatorDiscordId);

            db.AutoModRules.Add(new AutoModRuleEntity
            {
                DiscordId = e.Rule.Id,
                GuildId = guild?.Id ?? Guid.Empty,
                CreatorId = creator?.Id ?? Guid.Empty,
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
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleUpdatedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var guildDiscordId = e.Rule.Guild?.Id ?? 0;

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModRuleUpdatedRule", guildDiscordId, null, e.Rule.Creator?.Id);

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
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleDeletedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var guildDiscordId = e.Rule.Guild?.Id ?? 0;

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModRuleDeletedRule", guildDiscordId, null, e.Rule.Creator?.Id);

            await db.AutoModRules
                .Where(r => r.DiscordId == e.Rule.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted, true));

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
        }
    }
}
