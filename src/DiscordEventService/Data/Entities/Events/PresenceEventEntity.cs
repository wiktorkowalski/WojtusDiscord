using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public class PresenceEventEntity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GuildId { get; set; }
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

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public UserEntity? User { get; set; }
}
