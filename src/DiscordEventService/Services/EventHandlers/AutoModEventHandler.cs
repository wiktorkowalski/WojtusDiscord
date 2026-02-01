using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class AutoModEventHandler(IServiceScopeFactory scopeFactory, ILogger<AutoModEventHandler> logger) :
    IEventHandler<AutoModerationRuleCreatedEventArgs>,
    IEventHandler<AutoModerationRuleUpdatedEventArgs>,
    IEventHandler<AutoModerationRuleDeletedEventArgs>,
    IEventHandler<AutoModerationRuleExecutedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleCreatedEventArgs e)
    {
        try
        {
            if (e.Rule?.Guild is null) return;

            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModRuleCreated", e.Rule.Guild.Id, null, e.Rule.Creator?.Id);

            var entity = new AutoModEventEntity
            {
                GuildDiscordId = e.Rule.Guild.Id,
                RuleDiscordId = e.Rule.Id,
                EventType = AutoModEventType.RuleCreated,
                RuleName = e.Rule.Name,
                TriggerType = (int)e.Rule.TriggerType,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.AutoModEvents.Add(entity);
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
            if (e.Rule?.Guild is null) return;

            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModRuleUpdated", e.Rule.Guild.Id, null, e.Rule.Creator?.Id);

            var entity = new AutoModEventEntity
            {
                GuildDiscordId = e.Rule.Guild.Id,
                RuleDiscordId = e.Rule.Id,
                EventType = AutoModEventType.RuleUpdated,
                RuleName = e.Rule.Name,
                TriggerType = (int)e.Rule.TriggerType,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.AutoModEvents.Add(entity);
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
            if (e.Rule?.Guild is null) return;

            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModRuleDeleted", e.Rule.Guild.Id, null, e.Rule.Creator?.Id);

            var entity = new AutoModEventEntity
            {
                GuildDiscordId = e.Rule.Guild.Id,
                RuleDiscordId = e.Rule.Id,
                EventType = AutoModEventType.RuleDeleted,
                RuleName = e.Rule.Name,
                TriggerType = (int)e.Rule.TriggerType,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.AutoModEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling automod rule deleted");
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleExecutedEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "AutoModActionExecuted", e.Rule.GuildId, e.Rule.ChannelId, e.Rule.UserId);

            // Note: e.Rule is actually DiscordAutoModerationActionExecution
            var entity = new AutoModEventEntity
            {
                GuildDiscordId = e.Rule.GuildId,
                RuleDiscordId = e.Rule.RuleId,
                EventType = AutoModEventType.ActionExecuted,
                TriggerType = (int)e.Rule.TriggerType,
                UserDiscordId = e.Rule.UserId,
                ChannelDiscordId = e.Rule.ChannelId,
                MessageDiscordId = e.Rule.MessageId,
                AlertSystemMessageDiscordId = e.Rule.AlertSystemMessageId,
                Content = e.Rule.Content,
                MatchedKeyword = e.Rule.MatchedKeyword,
                MatchedContent = e.Rule.MatchedContent,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            };

            db.AutoModEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling automod action executed");
        }
    }
}
