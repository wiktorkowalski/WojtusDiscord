using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class AutoModEventHandler(EventPipeline pipeline) :
    IEventHandler<AutoModerationRuleCreatedEventArgs>,
    IEventHandler<AutoModerationRuleUpdatedEventArgs>,
    IEventHandler<AutoModerationRuleDeletedEventArgs>,
    IEventHandler<AutoModerationRuleExecutedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleCreatedEventArgs e)
    {
        if (e.Rule?.Guild is null) return;

        await pipeline.Execute(e, "AutoModRuleCreated", nameof(AutoModEventHandler),
            e.Rule.Guild.Id, null, e.Rule.Creator?.Id, async ctx =>
            {
                ctx.Db.AutoModEvents.Add(new AutoModEventEntity
                {
                    GuildDiscordId = e.Rule.Guild.Id,
                    RuleDiscordId = e.Rule.Id,
                    EventType = AutoModEventType.RuleCreated,
                    RuleName = e.Rule.Name,
                    TriggerType = (int)e.Rule.TriggerType,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleUpdatedEventArgs e)
    {
        if (e.Rule?.Guild is null) return;

        await pipeline.Execute(e, "AutoModRuleUpdated", nameof(AutoModEventHandler),
            e.Rule.Guild.Id, null, e.Rule.Creator?.Id, async ctx =>
            {
                ctx.Db.AutoModEvents.Add(new AutoModEventEntity
                {
                    GuildDiscordId = e.Rule.Guild.Id,
                    RuleDiscordId = e.Rule.Id,
                    EventType = AutoModEventType.RuleUpdated,
                    RuleName = e.Rule.Name,
                    TriggerType = (int)e.Rule.TriggerType,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleDeletedEventArgs e)
    {
        if (e.Rule?.Guild is null) return;

        await pipeline.Execute(e, "AutoModRuleDeleted", nameof(AutoModEventHandler),
            e.Rule.Guild.Id, null, e.Rule.Creator?.Id, async ctx =>
            {
                ctx.Db.AutoModEvents.Add(new AutoModEventEntity
                {
                    GuildDiscordId = e.Rule.Guild.Id,
                    RuleDiscordId = e.Rule.Id,
                    EventType = AutoModEventType.RuleDeleted,
                    RuleName = e.Rule.Name,
                    TriggerType = (int)e.Rule.TriggerType,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleExecutedEventArgs e)
    {
        await pipeline.Execute(e, "AutoModActionExecuted", nameof(AutoModEventHandler),
            e.Rule.GuildId, e.Rule.ChannelId, e.Rule.UserId, async ctx =>
            {
                // e.Rule is a DiscordAutoModerationActionExecution, not a rule definition.
                ctx.Db.AutoModEvents.Add(new AutoModEventEntity
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
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
