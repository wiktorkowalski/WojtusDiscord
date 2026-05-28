using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public sealed class AuditLogEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildAuditLogCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildAuditLogCreatedEventArgs e)
    {
        await pipeline.Execute(e, "GuildAuditLogCreated", nameof(AuditLogEventHandler),
            e.Guild.Id, null, e.AuditLogEntry.UserResponsible?.Id, async ctx =>
            {
                ctx.Db.AuditLogEvents.Add(new AuditLogEventEntity
                {
                    AuditLogDiscordId = e.AuditLogEntry.Id,
                    GuildDiscordId = e.Guild.Id,
                    UserDiscordId = e.AuditLogEntry.UserResponsible?.Id,
                    TargetDiscordId = null,
                    ActionType = (int)e.AuditLogEntry.ActionType,
                    Reason = e.AuditLogEntry.Reason,
                    ChangesJson = null,
                    OptionsJson = null,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
