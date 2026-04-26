using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class AuditLogEventHandler(IServiceScopeFactory scopeFactory, ILogger<AuditLogEventHandler> logger) :
    IEventHandler<GuildAuditLogCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildAuditLogCreatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "GuildAuditLogCreated", e.Guild.Id, null, e.AuditLogEntry.UserResponsible?.Id);

            db.AuditLogEvents.Add(new AuditLogEventEntity
            {
                AuditLogDiscordId = e.AuditLogEntry.Id,
                GuildDiscordId = e.Guild.Id,
                UserDiscordId = e.AuditLogEntry.UserResponsible?.Id,
                TargetDiscordId = null,
                ActionType = (int)e.AuditLogEntry.ActionType,
                Reason = e.AuditLogEntry.Reason,
                ChangesJson = null,
                OptionsJson = null,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling audit log entry");
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "GuildAuditLogCreated", nameof(AuditLogEventHandler), ex,
                e.Guild?.Id, null, e.AuditLogEntry?.UserResponsible?.Id, rawJson);
        }
    }
}
