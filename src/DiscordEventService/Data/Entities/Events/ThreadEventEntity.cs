namespace DiscordEventService.Data.Entities.Events;

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

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }
}
