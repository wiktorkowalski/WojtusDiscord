using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public sealed class AutoModRuleEventHandler(EventPipeline pipeline) :
    IEventHandler<AutoModerationRuleCreatedEventArgs>,
    IEventHandler<AutoModerationRuleUpdatedEventArgs>,
    IEventHandler<AutoModerationRuleDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleCreatedEventArgs e)
    {
        var guildDiscordId = e.Rule.Guild?.Id ?? 0UL;
        var creatorDiscordId = e.Rule.Creator?.Id ?? 0UL;

        await pipeline.Execute(e, "AutoModRuleCreatedRule", nameof(AutoModRuleEventHandler),
            guildDiscordId, null, creatorDiscordId, async ctx =>
            {
                Guid? guildGuid = null;
                if (e.Rule.Guild != null)
                {
                    var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                    var id = await guildUpsert.UpsertGuildAsync(e.Rule.Guild);
                    guildGuid = id != Guid.Empty ? id : null;
                }

                Guid? creatorGuid = null;
                if (e.Rule.Creator != null)
                {
                    var userService = ctx.Services.GetRequiredService<UserService>();
                    await userService.UpsertUserAsync(e.Rule.Creator);
                    creatorGuid = await ctx.Db.Users
                        .Where(u => u.DiscordId == e.Rule.Creator.Id)
                        .Select(u => (Guid?)u.Id)
                        .FirstOrDefaultAsync();
                }

                ctx.Db.AutoModRules.Add(new AutoModRuleEntity
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

                ctx.Db.AutoModRuleEvents.Add(new AutoModRuleEventEntity
                {
                    RuleDiscordId = e.Rule.Id,
                    GuildDiscordId = guildDiscordId,
                    CreatorDiscordId = creatorDiscordId,
                    EventType = AutoModRuleEventType.Created,
                    Name = e.Rule.Name ?? string.Empty,
                    TriggerType = (int)e.Rule.TriggerType,
                    IsEnabled = e.Rule.IsEnabled,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleUpdatedEventArgs e)
    {
        var guildDiscordId = e.Rule.Guild?.Id ?? 0UL;

        await pipeline.Execute(e, "AutoModRuleUpdatedRule", nameof(AutoModRuleEventHandler),
            guildDiscordId, null, e.Rule.Creator?.Id, async ctx =>
            {
                await ctx.Db.AutoModRules
                    .Where(r => r.DiscordId == e.Rule.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Name, e.Rule.Name ?? string.Empty)
                        .SetProperty(r => r.IsEnabled, e.Rule.IsEnabled)
                        .SetProperty(r => r.ActionsJson, e.Rule.Actions != null ? JsonSerializer.Serialize(e.Rule.Actions) : null));

                ctx.Db.AutoModRuleEvents.Add(new AutoModRuleEventEntity
                {
                    RuleDiscordId = e.Rule.Id,
                    GuildDiscordId = guildDiscordId,
                    CreatorDiscordId = e.Rule.Creator?.Id ?? 0,
                    EventType = AutoModRuleEventType.Updated,
                    Name = e.Rule.Name ?? string.Empty,
                    TriggerType = (int)e.Rule.TriggerType,
                    IsEnabled = e.Rule.IsEnabled,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, AutoModerationRuleDeletedEventArgs e)
    {
        var guildDiscordId = e.Rule.Guild?.Id ?? 0UL;

        await pipeline.Execute(e, "AutoModRuleDeletedRule", nameof(AutoModRuleEventHandler),
            guildDiscordId, null, e.Rule.Creator?.Id, async ctx =>
            {
                await ctx.Db.AutoModRules
                    .Where(r => r.DiscordId == e.Rule.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.IsDeleted, true)
                        .SetProperty(r => r.DeletedAtUtc, (DateTime?)ctx.ReceivedAtUtc));

                ctx.Db.AutoModRuleEvents.Add(new AutoModRuleEventEntity
                {
                    RuleDiscordId = e.Rule.Id,
                    GuildDiscordId = guildDiscordId,
                    CreatorDiscordId = e.Rule.Creator?.Id ?? 0,
                    EventType = AutoModRuleEventType.Deleted,
                    Name = e.Rule.Name ?? string.Empty,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
