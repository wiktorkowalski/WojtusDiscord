using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum ChannelEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    PinsUpdated = 3
}

public class ChannelEventEntity
{
    public Guid Id { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? GuildId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public int ChannelType { get; set; }
    public ChannelEventType EventType { get; set; }
    public string? NameBefore { get; set; }
    public string? NameAfter { get; set; }
    public string? TopicBefore { get; set; }
    public string? TopicAfter { get; set; }
    public int? PositionBefore { get; set; }
    public int? PositionAfter { get; set; }
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
}
