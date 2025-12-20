using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum MemberEventType
{
    Joined = 0,
    Left = 1,
    Updated = 2,
    Banned = 3,
    Unbanned = 4
}

public class MemberEventEntity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GuildId { get; set; }
    public ulong UserDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public MemberEventType EventType { get; set; }
    public string? NicknameBefore { get; set; }
    public string? NicknameAfter { get; set; }
    public string? RolesAddedJson { get; set; }
    public string? RolesRemovedJson { get; set; }
    public DateTime? TimeoutUntilUtc { get; set; }
    public string? BanReason { get; set; }
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public UserEntity? User { get; set; }
}
