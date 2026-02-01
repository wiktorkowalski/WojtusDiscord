using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Services;

public class FailedEventService(DiscordDbContext db, ILogger<FailedEventService> logger)
{
    public async Task RecordFailureAsync(
        string eventType,
        string handlerName,
        Exception exception,
        ulong? guildId = null,
        ulong? channelId = null,
        ulong? userId = null,
        string? eventJson = null,
        DateTime? eventReceivedAt = null)
    {
        try
        {
            var failedEvent = new FailedEventEntity
            {
                EventType = eventType,
                HandlerName = handlerName,
                GuildDiscordId = guildId,
                ChannelDiscordId = channelId,
                UserDiscordId = userId,
                EventJson = eventJson,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                ExceptionMessage = exception.Message,
                StackTrace = exception.StackTrace,
                FailedAtUtc = DateTime.UtcNow,
                EventReceivedAtUtc = eventReceivedAt ?? DateTime.UtcNow
            };

            await db.FailedEvents.AddAsync(failedEvent);
            await db.SaveChangesAsync();

            logger.LogWarning(
                "Recorded failed event: {EventType} in {HandlerName} - {ExceptionType}: {Message}",
                eventType, handlerName, failedEvent.ExceptionType, exception.Message);
        }
        catch (Exception ex)
        {
            // Last resort - if we can't even record the failure, just log it
            logger.LogCritical(ex,
                "CRITICAL: Failed to record event failure. Original error: {EventType} in {HandlerName} - {OriginalException}",
                eventType, handlerName, exception.Message);
        }
    }
}
