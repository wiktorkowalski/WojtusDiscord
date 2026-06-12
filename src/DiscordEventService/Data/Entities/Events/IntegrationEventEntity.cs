namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum IntegrationEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class IntegrationEventEntity
{
    public Guid Id { get; set; }
    public ulong IntegrationDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public IntegrationEventType EventType { get; set; }

    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool? IsEnabled { get; set; }
    public ulong? ApplicationDiscordId { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
