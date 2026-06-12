using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class VoiceEventHandler(EventPipeline pipeline) :
    IEventHandler<VoiceStateUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)
    {
        var eventType = DetermineEventType(args);

        await pipeline.Execute(args, "VoiceStateUpdated", nameof(VoiceEventHandler),
            args.Guild.Id, args.After?.Channel?.Id, args.User.Id, async ctx =>
            {
                ctx.Db.VoiceStateEvents.Add(new VoiceStateEventEntity
                {
                    UserDiscordId = args.User.Id,
                    GuildDiscordId = args.Guild.Id,
                    ChannelDiscordIdBefore = args.Before?.Channel?.Id,
                    ChannelDiscordIdAfter = args.After?.Channel?.Id,
                    EventType = eventType,

                    WasSelfMuted = args.Before?.IsSelfMuted ?? false,
                    WasSelfDeafened = args.Before?.IsSelfDeafened ?? false,
                    WasServerMuted = args.Before?.IsServerMuted ?? false,
                    WasServerDeafened = args.Before?.IsServerDeafened ?? false,
                    WasStreaming = args.Before?.IsSelfStream ?? false,
                    WasVideo = args.Before?.IsSelfVideo ?? false,
                    WasSuppressed = args.Before?.IsSuppressed ?? false,

                    IsSelfMuted = args.After?.IsSelfMuted ?? false,
                    IsSelfDeafened = args.After?.IsSelfDeafened ?? false,
                    IsServerMuted = args.After?.IsServerMuted ?? false,
                    IsServerDeafened = args.After?.IsServerDeafened ?? false,
                    IsStreaming = args.After?.IsSelfStream ?? false,
                    IsVideo = args.After?.IsSelfVideo ?? false,
                    IsSuppressed = args.After?.IsSuppressed ?? false,

                    SessionId = args.After?.SessionId,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();

                ctx.Logger.LogDebug("Recorded voice event: {EventType} for user {UserId} in guild {GuildId}",
                    eventType, args.User.Id, args.Guild.Id);
            });
    }

    private static VoiceEventType DetermineEventType(VoiceStateUpdatedEventArgs args)
    {
        var beforeChannelId = args.Before?.Channel?.Id;
        var afterChannelId = args.After?.Channel?.Id;

        if (beforeChannelId == null && afterChannelId != null)
            return VoiceEventType.Joined;

        if (beforeChannelId != null && afterChannelId == null)
            return VoiceEventType.Left;

        if (beforeChannelId != null && afterChannelId != null && beforeChannelId != afterChannelId)
            return VoiceEventType.Moved;

        return VoiceEventType.StateChanged;
    }
}
