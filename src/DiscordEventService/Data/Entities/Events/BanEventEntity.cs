using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum BanEventType
{
    Added = 0,
    Removed = 1
}

public class BanEventEntity
{
    public Guid Id { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? UserId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong UserDiscordId { get; set; }
    public BanEventType EventType { get; set; }
    public string? Reason { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public UserEntity? User { get; set; }
}
