namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum RoleEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class RoleEventEntity
{
    public Guid Id { get; set; }
    public ulong RoleDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public RoleEventType EventType { get; set; }
    public string? NameBefore { get; set; }
    public string? NameAfter { get; set; }
    public int? ColorBefore { get; set; }
    public int? ColorAfter { get; set; }
    public long? PermissionsBefore { get; set; }
    public long? PermissionsAfter { get; set; }
    // DSharpPlus does not expose a per-event timestamp for role events; equals ReceivedAtUtc by design.
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
