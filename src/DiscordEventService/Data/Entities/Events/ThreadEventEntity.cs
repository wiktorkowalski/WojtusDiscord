namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum ThreadEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    MembersUpdated = 3
}

public class ThreadEventEntity
{
    public Guid Id { get; set; }
    public ulong ThreadDiscordId { get; set; }
    public ulong ParentChannelDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ThreadEventType EventType { get; set; }
    public string? Name { get; set; }
    public ulong? OwnerDiscordId { get; set; }
    public ulong? StarterMessageDiscordId { get; set; }
    public bool IsArchived { get; set; }
    public bool IsLocked { get; set; }
    public string? MembersAddedJson { get; set; }
    public string? MembersRemovedJson { get; set; }
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
