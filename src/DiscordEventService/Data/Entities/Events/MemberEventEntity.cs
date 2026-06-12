namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
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
    public ulong UserDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public MemberEventType EventType { get; set; }
    public string? NicknameBefore { get; set; }
    public string? NicknameAfter { get; set; }
    public string? RolesAddedJson { get; set; }
    public string? RolesRemovedJson { get; set; }
    public DateTime? TimeoutUntilUtc { get; set; }
    public DateTime? PremiumSinceBeforeUtc { get; set; }
    public DateTime? PremiumSinceAfterUtc { get; set; }
    public string? GuildAvatarHashBefore { get; set; }
    public string? GuildAvatarHashAfter { get; set; }
    public bool? IsPendingBefore { get; set; }
    public bool? IsPendingAfter { get; set; }
    public bool? IsMutedBefore { get; set; }
    public bool? IsMutedAfter { get; set; }
    public bool? IsDeafenedBefore { get; set; }
    public bool? IsDeafenedAfter { get; set; }
    public string? BanReason { get; set; }
    // For Joined, from Member.JoinedAt. For Left/Updated/Banned/Unbanned, DSharpPlus exposes no per-event
    // timestamp; equals ReceivedAtUtc by design.
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
