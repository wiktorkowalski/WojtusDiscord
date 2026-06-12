namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum PresenceEventType
{
    Updated = 0,
    BootSnapshot = 1
}

public class PresenceEventEntity
{
    public Guid Id { get; set; }
    public ulong UserDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public PresenceEventType EventType { get; set; }

    // Before status
    public int DesktopStatusBefore { get; set; }
    public int MobileStatusBefore { get; set; }
    public int WebStatusBefore { get; set; }

    // After status
    public int DesktopStatusAfter { get; set; }
    public int MobileStatusAfter { get; set; }
    public int WebStatusAfter { get; set; }

    // Activities (stored as JSON)
    public string? ActivitiesBeforeJson { get; set; }
    public string? ActivitiesAfterJson { get; set; }

    // DSharpPlus does not expose a per-event timestamp for presence updates; equals ReceivedAtUtc by design.
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
