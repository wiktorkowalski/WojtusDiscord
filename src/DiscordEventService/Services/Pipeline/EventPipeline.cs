using DiscordEventService.Data;

namespace DiscordEventService.Services.Pipeline;

public sealed class EventPipeline(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
{
    public async Task Execute<TEventArgs>(
        TEventArgs e,
        string eventType,
        string handlerName,
        ulong guildId,
        ulong? channelId,
        ulong? userId,
        Func<EventContext, Task> handler) where TEventArgs : class
    {
        var logger = loggerFactory.CreateLogger(handlerName);
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;

            // Records a FailedEvent in an isolated scope using this event's metadata. Used both
            // by the pipeline's own exception path and by handlers that want to record a soft
            // failure (e.g. an unresolved FK) and return without throwing.
            async Task RecordFailureAsync(Exception ex)
            {
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    eventType, handlerName, ex,
                    guildId, channelId, userId, rawJson, correlationId: correlationId);
            }

            try
            {
                var receivedAt = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    e, eventType, guildId, channelId, userId, correlationId: correlationId);

                // Flush the raw event row before handler logic. Any ChangeTracker.Clear()
                // in upsert services would silently drop staged raw_event_logs rows otherwise.
                await db.SaveChangesAsync();

                var context = new EventContext(db, scope.ServiceProvider, correlationId, rawJson, receivedAt, logger, RecordFailureAsync);
                await handler(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling {EventType} in {HandlerName}", eventType, handlerName);
                await RecordFailureAsync(ex);
            }
        }
    }
}
