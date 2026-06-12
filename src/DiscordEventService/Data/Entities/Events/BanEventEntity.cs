namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum BanEventType
{
    Added = 0,
    Removed = 1
}

public class BanEventEntity
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong UserDiscordId { get; set; }
    public BanEventType EventType { get; set; }
    public string? Reason { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
