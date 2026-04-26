using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Services;

public class FailedEventService(DiscordDbContext db, ILogger<FailedEventService> logger, IHostEnvironment env)
{
    private static readonly SemaphoreSlim _fallbackFileLock = new(1, 1);

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

            try
            {
                var failedAtUtc = DateTime.UtcNow;
                var logsDir = Path.Combine(env.ContentRootPath, "logs");
                Directory.CreateDirectory(logsDir);
                var path = Path.Combine(logsDir, $"dead-letter-fallback-{failedAtUtc:yyyy-MM-dd}.jsonl");
                var record = new
                {
                    failedAtUtc,
                    eventType,
                    handlerName,
                    guildId,
                    channelId,
                    userId,
                    exceptionType = exception.GetType().FullName,
                    exceptionMessage = exception.Message,
                    stackTrace = exception.StackTrace,
                    eventJson,
                    secondaryExceptionType = ex.GetType().FullName,
                    secondaryExceptionMessage = ex.Message
                };
                var line = JsonSerializer.Serialize(record);
                await _fallbackFileLock.WaitAsync();
                try
                {
                    await File.AppendAllTextAsync(path, line + Environment.NewLine);
                }
                finally
                {
                    _fallbackFileLock.Release();
                }
            }
            catch (Exception fallbackEx)
            {
                logger.LogCritical(fallbackEx,
                    "JSONL fallback also failed for {EventType} in {HandlerName}",
                    eventType, handlerName);
            }
        }
    }
}
