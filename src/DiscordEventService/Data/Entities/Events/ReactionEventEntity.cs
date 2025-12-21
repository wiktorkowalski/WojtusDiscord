using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum ReactionEventType
{
    Added = 0,
    Removed = 1,
    Cleared = 2,
    EmojiCleared = 3,
    Backfilled = 4
}

public class ReactionEventEntity
{
    public Guid Id { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GuildId { get; set; }
    public ulong MessageDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong UserDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong? EmoteDiscordId { get; set; }
    public string EmoteName { get; set; } = string.Empty;
    public bool IsAnimated { get; set; }
    public bool IsBurst { get; set; }
    public ReactionEventType EventType { get; set; }
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
    public UserEntity? User { get; set; }
    public MessageEntity? Message { get; set; }
}
