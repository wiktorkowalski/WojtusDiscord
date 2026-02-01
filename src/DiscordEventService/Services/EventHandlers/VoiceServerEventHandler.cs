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
        try
        {
            var now = DateTime.UtcNow;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            // TODO: VoiceServerUpdatedEventArgs contains a Token property that may be serialized to raw JSON.
            // Investigate if this is a security concern and consider excluding it from serialization.
            var rawJson = await rawEventService.SerializeAndLogAsync(
                args, "VoiceServerUpdated", args.Guild.Id, null, null);

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
                args.Guild.Id, null, null);
        }
    }
}
