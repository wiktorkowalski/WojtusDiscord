namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum StageInstanceEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class StageInstanceEventEntity
{
    public Guid Id { get; set; }
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

    // Raw serialized event args from DSharpPlus for debugging.
    public string? RawEventJson { get; set; }
}
