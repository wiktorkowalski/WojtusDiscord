using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum RoleEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class RoleEventEntity
{
    public Guid Id { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? GuildId { get; set; }
    public ulong RoleDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public RoleEventType EventType { get; set; }
    public string? NameBefore { get; set; }
    public string? NameAfter { get; set; }
    public int? ColorBefore { get; set; }
    public int? ColorAfter { get; set; }
    public long? PermissionsBefore { get; set; }
    public long? PermissionsAfter { get; set; }
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public RoleEntity? Role { get; set; }
}
