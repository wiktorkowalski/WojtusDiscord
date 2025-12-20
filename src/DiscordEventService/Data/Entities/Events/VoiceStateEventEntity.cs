using DiscordEventService.Data.Entities.Core;

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
    public Guid? UserId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? ChannelIdBefore { get; set; }
    public Guid? ChannelIdAfter { get; set; }
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
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public UserEntity? User { get; set; }
    public ChannelEntity? ChannelBefore { get; set; }
    public ChannelEntity? ChannelAfter { get; set; }
}
