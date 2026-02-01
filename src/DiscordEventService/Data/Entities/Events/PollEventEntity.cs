using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum PollEventType
{
    VoteAdded = 0,
    VoteRemoved = 1
}

public class PollEventEntity
{
    public Guid Id { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? UserId { get; set; }
    public ulong MessageDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong UserDiscordId { get; set; }
    public int AnswerId { get; set; }
    public PollEventType EventType { get; set; }

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
