using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class VoiceEventHandler(EventPipeline pipeline) :
    IEventHandler<VoiceStateUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, VoiceStateUpdatedEventArgs args)
    {
        var eventType = DetermineEventType(args);

        // From the event alone, before any database work: an event the pipeline fails still counts.
        foreach (var change in ChangesOf(eventType, FlagsOf(args.Before), FlagsOf(args.After)))
            BotMetrics.VoiceStateChanged(change);

        await pipeline.ExecuteAsync(args, "VoiceStateUpdated", nameof(VoiceEventHandler),
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

    // The label values of wojtus_voice_state_changes_total for one event. A join or a leave is
    // that one change: the flags of a state that did not exist are not a change. A move or a
    // change in place gives one value per flag that flipped, and "other" when none of the four
    // did (a suppress change, a new session id).
    internal static IReadOnlyList<string> ChangesOf(VoiceEventType eventType, VoiceFlags before, VoiceFlags after)
    {
        switch (eventType)
        {
            case VoiceEventType.Joined:
                return ["join"];
            case VoiceEventType.Left:
                return ["leave"];
        }

        List<string> changes = [];
        if (eventType == VoiceEventType.Moved)
            changes.Add("move");
        if (before.Muted != after.Muted)
            changes.Add(after.Muted ? "mute" : "unmute");
        if (before.Deafened != after.Deafened)
            changes.Add(after.Deafened ? "deafen" : "undeafen");
        if (before.Streaming != after.Streaming)
            changes.Add(after.Streaming ? "stream_start" : "stream_stop");
        if (before.Video != after.Video)
            changes.Add(after.Video ? "video_start" : "video_stop");
        if (changes.Count == 0)
            changes.Add("other");

        return changes;
    }

    // Muted and deafened: by the member or by the server, the metric does not tell them apart.
    // No state (before a join, after a leave) = every flag off.
    private static VoiceFlags FlagsOf(DiscordVoiceState? state) => state is null
        ? default
        : new VoiceFlags(
            state.IsSelfMuted || state.IsServerMuted,
            state.IsSelfDeafened || state.IsServerDeafened,
            state.IsSelfStream,
            state.IsSelfVideo);

    internal readonly record struct VoiceFlags(bool Muted, bool Deafened, bool Streaming, bool Video);

    private static VoiceEventType DetermineEventType(VoiceStateUpdatedEventArgs args)
    {
        var beforeChannelId = args.Before?.Channel?.Id;
        var afterChannelId = args.After?.Channel?.Id;

        return (beforeChannelId, afterChannelId) switch
        {
            (null, not null) => VoiceEventType.Joined,
            (not null, null) => VoiceEventType.Left,
            (not null, not null) when beforeChannelId != afterChannelId => VoiceEventType.Moved,
            _ => VoiceEventType.StateChanged,
        };
    }
}
