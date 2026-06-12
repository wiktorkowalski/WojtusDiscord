using DiscordEventService.Data;

namespace DiscordEventService.Services.Pipeline;

internal sealed class EventPipeline(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
{
    public async Task ExecuteAsync<TEventArgs>(
        TEventArgs e,
        string eventType,
        string handlerName,
        ulong guildId,
        ulong? channelId,
        ulong? userId,
        Func<EventContext, Task> handler,
        bool logRawEvent = true) where TEventArgs : class
    {
        var logger = loggerFactory.CreateLogger(handlerName);
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;

            // Isolated scope so a soft failure can be recorded even after the handler's scope faulted.
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

                // Some events are already raw-logged by a sibling handler (e.g. GuildUpdated by
                // GuildUpdateEventHandler); pass logRawEvent: false to skip a duplicate row.
                if (logRawEvent)
                {
                    var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();
                    var serialized = await rawEventService.SerializeAndLogAsync(
                        e, eventType, guildId, channelId, userId, correlationId: correlationId);
                    rawJson = serialized.Json;

                    // Flush the raw event row before handler logic. Any ChangeTracker.Clear()
                    // in upsert services would silently drop staged raw_event_logs rows otherwise.
                    await db.SaveChangesAsync();

                    // Serialization failure means raw_event_logs holds an unreplayable stub. The
                    // row is flagged (serialization_failed=true); surface it loudly and record a
                    // FailedEvent so it is alertable. The handler still runs — it acts on the live
                    // event args, not the JSON, so structured ingestion is unaffected.
                    if (serialized.Error is not null)
                    {
                        logger.LogError(serialized.Error,
                            "Failed to serialize {EventType} in {HandlerName}; stored a flagged stub in raw_event_logs (payload unrecoverable)",
                            eventType, handlerName);
                        await RecordFailureAsync(serialized.Error);
                    }
                }

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
