using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class UserEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? GlobalName { get; set; }
    public string? Discriminator { get; set; }
    public string? AvatarHash { get; set; }
    public bool IsBot { get; set; }
    public bool IsSystem { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public ICollection<MemberEntity> Memberships { get; set; } = [];
    public ICollection<ActivityEntity> Activities { get; set; } = [];

    // Navigation properties - Events (soft relations)
    public ICollection<MessageEventEntity> MessageEvents { get; set; } = [];
    public ICollection<ReactionEventEntity> ReactionEvents { get; set; } = [];
    public ICollection<VoiceStateEventEntity> VoiceStateEvents { get; set; } = [];
    public ICollection<PresenceEventEntity> PresenceEvents { get; set; } = [];
    public ICollection<MemberEventEntity> MemberEvents { get; set; } = [];
    public ICollection<BanEventEntity> BanEvents { get; set; } = [];
    public ICollection<PollEventEntity> PollEvents { get; set; } = [];
    public ICollection<AutoModEventEntity> AutoModEvents { get; set; } = [];
    public ICollection<InviteEventEntity> InviteEvents { get; set; } = [];
    public ICollection<TypingEventEntity> TypingEvents { get; set; } = [];
    public ICollection<AuditLogEventEntity> AuditLogEvents { get; set; } = [];
}
