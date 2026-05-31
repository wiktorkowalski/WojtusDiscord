using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;

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
        DateTime? eventReceivedAt = null,
        Guid? correlationId = null)
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
                EventReceivedAtUtc = eventReceivedAt ?? DateTime.UtcNow,
                CorrelationId = correlationId
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

    /// <summary>
    /// Acknowledges/resolves a failed event so the <c>HealthCheckJob</c> alert (which counts
    /// <c>!IsResolved</c> rows in a window) can clear by explicit operator action rather than only
    /// by aging out. Idempotent: filters on <c>!IsResolved</c>, so re-resolving or a missing id is a
    /// no-op returning <c>false</c>. A single set-based update — no transaction ceremony.
    /// </summary>
    /// <remarks>
    /// This is the feasible dead-letter kernel (ack/annotate). Automatic replay via
    /// <c>EventJson</c> reconstruction is deliberately NOT implemented: that payload is lossy
    /// (BypassRecursiveConverterResolver, MaxDepth, snowflake-dict shape) and DSharpPlus
    /// <c>*EventArgs</c> have no deserialization path, so a handler cannot be re-driven from it
    /// (deferred — OrphanReplayService OD#5). <see cref="FailedEventEntity.RetryCount"/> stays
    /// reserved for a future per-event-type replayer that re-drives from current state.
    /// Unresolved rows are the manual-review bucket; record transient-vs-permanent context in
    /// <paramref name="notes"/>.
    /// </remarks>
    public async Task<bool> ResolveAsync(Guid id, string? notes, CancellationToken cancellationToken = default)
    {
        var resolved = await db.FailedEvents
            .Where(f => f.Id == id && !f.IsResolved)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.IsResolved, true)
                .SetProperty(f => f.ResolvedAtUtc, DateTime.UtcNow)
                .SetProperty(f => f.ResolutionNotes, notes), cancellationToken);

        if (resolved > 0)
            logger.LogInformation("Resolved failed event {FailedEventId}", id);

        return resolved > 0;
    }
}
