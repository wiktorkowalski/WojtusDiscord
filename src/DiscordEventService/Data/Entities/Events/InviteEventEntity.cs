using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum InviteEventType
{
    Created = 0,
    Deleted = 1
}

public class InviteEventEntity
{
    public Guid Id { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? InviterId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong? InviterDiscordId { get; set; }
    public InviteEventType EventType { get; set; }

    public string? Code { get; set; }
    public int? MaxAge { get; set; }
    public int? MaxUses { get; set; }
    public bool IsTemporary { get; set; }
    public int? Uses { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
    public UserEntity? Inviter { get; set; }
}
