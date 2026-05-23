namespace DiscordEventService.Data.Entities.Events;

public class PresenceEventEntity
{
    public Guid Id { get; set; }
    public ulong UserDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }

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

    /// <summary>DSharpPlus does not expose a per-event timestamp for presence updates; equals ReceivedAtUtc by design.</summary>
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }
}
