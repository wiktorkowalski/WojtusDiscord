using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class VoiceEventHandler(IServiceScopeFactory scopeFactory, ILogger<VoiceEventHandler> logger) :
    IEventHandler<VoiceStateUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)
    {
        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                var now = DateTime.UtcNow;
                var eventType = DetermineEventType(args);

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                var rawJson = await rawEventService.SerializeAndLogAsync(
                    args, "VoiceStateUpdated", args.Guild.Id, args.After?.Channel?.Id, args.User.Id, correlationId: correlationId);

                var voiceEvent = new VoiceStateEventEntity
                {
                    UserDiscordId = args.User.Id,
                    GuildDiscordId = args.Guild.Id,
                    ChannelDiscordIdBefore = args.Before?.Channel?.Id,
                    ChannelDiscordIdAfter = args.After?.Channel?.Id,
                    EventType = eventType,

                    // Before state flags
                    WasSelfMuted = args.Before?.IsSelfMuted ?? false,
                    WasSelfDeafened = args.Before?.IsSelfDeafened ?? false,
                    WasServerMuted = args.Before?.IsServerMuted ?? false,
                    WasServerDeafened = args.Before?.IsServerDeafened ?? false,
                    WasStreaming = args.Before?.IsSelfStream ?? false,
                    WasVideo = args.Before?.IsSelfVideo ?? false,
                    WasSuppressed = args.Before?.IsSuppressed ?? false,

                    // After state flags
                    IsSelfMuted = args.After?.IsSelfMuted ?? false,
                    IsSelfDeafened = args.After?.IsSelfDeafened ?? false,
                    IsServerMuted = args.After?.IsServerMuted ?? false,
                    IsServerDeafened = args.After?.IsServerDeafened ?? false,
                    IsStreaming = args.After?.IsSelfStream ?? false,
                    IsVideo = args.After?.IsSelfVideo ?? false,
                    IsSuppressed = args.After?.IsSuppressed ?? false,

                    SessionId = args.After?.SessionId,
                    EventTimestampUtc = now,
                    ReceivedAtUtc = now,
                    RawEventJson = rawJson
                };

                db.VoiceStateEvents.Add(voiceEvent);
                await db.SaveChangesAsync();

                logger.LogDebug("Recorded voice event: {EventType} for user {UserId} in guild {GuildId}",
                    eventType, args.User.Id, args.Guild.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling voice state update for user {UserId} in guild {GuildId}",
                    args.User.Id, args.Guild.Id);
                using var failureScope = scopeFactory.CreateScope();
                var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "VoiceStateUpdated", nameof(VoiceEventHandler), ex,
                    args.Guild.Id, args.After?.Channel?.Id, args.User.Id, correlationId: correlationId);
            }
        }
    }

    private static VoiceEventType DetermineEventType(VoiceStateUpdatedEventArgs args)
    {
        var beforeChannelId = args.Before?.Channel?.Id;
        var afterChannelId = args.After?.Channel?.Id;

        // Joined: before channel null, after not null
        if (beforeChannelId == null && afterChannelId != null)
            return VoiceEventType.Joined;

        // Left: before not null, after null
        if (beforeChannelId != null && afterChannelId == null)
            return VoiceEventType.Left;

        // Moved: both not null, different
        if (beforeChannelId != null && afterChannelId != null && beforeChannelId != afterChannelId)
            return VoiceEventType.Moved;

        // StateChanged: same channel, flags changed
        return VoiceEventType.StateChanged;
    }
}
