using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class VoiceServerEventHandler(IServiceScopeFactory scopeFactory, ILogger<VoiceServerEventHandler> logger) :
    IEventHandler<VoiceServerUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, VoiceServerUpdatedEventArgs args)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            string? rawJson = null;
            try
            {
                var now = DateTime.UtcNow;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                rawJson = await rawEventService.SerializeAndLogAsync(
                    args, "VoiceServerUpdated", args.Guild.Id, null, null, correlationId: correlationId);

                var voiceServerEvent = new VoiceServerEventEntity
                {
                    GuildDiscordId = args.Guild.Id,
                    Endpoint = args.Endpoint,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now,
                    RawEventJson = rawJson
                };

                await db.VoiceServerEvents.AddAsync(voiceServerEvent);
                await db.SaveChangesAsync();

                logger.LogDebug("Recorded voice server event for guild {GuildId}, endpoint {Endpoint}",
                    args.Guild.Id, args.Endpoint);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling voice server update for guild {GuildId}", args.Guild.Id);
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "VoiceServerUpdated", nameof(VoiceServerEventHandler), ex,
                    args.Guild.Id, null, null, eventJson: rawJson, correlationId: correlationId);
            }
        }
    }
}
