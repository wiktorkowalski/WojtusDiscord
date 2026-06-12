using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class AuditLogEventHandler(EventPipeline pipeline) :
    IEventHandler<GuildAuditLogCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildAuditLogCreatedEventArgs e)
    {
        await pipeline.ExecuteAsync(e, "GuildAuditLogCreated", nameof(AuditLogEventHandler),
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
                    // The entry's snowflake encodes the server-side action time — the true Discord
                    // event time (CONTEXT.md). Fall back to receive time only if the Id is absent.
                    EventTimestampUtc = e.AuditLogEntry.Id != 0
                        ? e.AuditLogEntry.CreationTimestamp.UtcDateTime
                        : ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
