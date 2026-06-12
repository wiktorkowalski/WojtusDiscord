namespace DiscordEventService.Data.Entities.Events;

public enum VoiceEventType
{
    Joined = 0,
    Left = 1,
    Moved = 2,
    StateChanged = 3
}

public class VoiceStateEventEntity
{
    public Guid Id { get; set; }
    public ulong UserDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong? ChannelDiscordIdBefore { get; set; }
    public ulong? ChannelDiscordIdAfter { get; set; }
    public VoiceEventType EventType { get; set; }

    // Before state flags
    public bool WasSelfMuted { get; set; }
    public bool WasSelfDeafened { get; set; }
    public bool WasServerMuted { get; set; }
    public bool WasServerDeafened { get; set; }
    public bool WasStreaming { get; set; }
    public bool WasVideo { get; set; }
    public bool WasSuppressed { get; set; }

    // After state flags
    public bool IsSelfMuted { get; set; }
    public bool IsSelfDeafened { get; set; }
    public bool IsServerMuted { get; set; }
    public bool IsServerDeafened { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsVideo { get; set; }
    public bool IsSuppressed { get; set; }

    public string? SessionId { get; set; }
    // DSharpPlus does not expose a per-event timestamp for voice state updates; equals ReceivedAtUtc by design.
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
