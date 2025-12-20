using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum GuildEventType
{
    Updated = 0,
    Available = 1,
    Unavailable = 2
}

public class GuildEventEntity
{
    public Guid Id { get; set; }
    public Guid? GuildId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public GuildEventType EventType { get; set; }

    public string? NameBefore { get; set; }
    public string? NameAfter { get; set; }
    public string? IconHashBefore { get; set; }
    public string? IconHashAfter { get; set; }
    public ulong? OwnerDiscordIdBefore { get; set; }
    public ulong? OwnerDiscordIdAfter { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
}
