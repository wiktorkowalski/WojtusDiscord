using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum StageInstanceEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class StageInstanceEventEntity
{
    public Guid Id { get; set; }
    public Guid? StageInstanceId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? ChannelId { get; set; }
    public ulong StageInstanceDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public StageInstanceEventType EventType { get; set; }

    public string? TopicBefore { get; set; }
    public string? TopicAfter { get; set; }
    public int? PrivacyLevelBefore { get; set; }
    public int? PrivacyLevelAfter { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
    public StageInstanceEntity? StageInstance { get; set; }
}
