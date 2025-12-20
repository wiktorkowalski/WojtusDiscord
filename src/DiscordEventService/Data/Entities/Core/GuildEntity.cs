using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class GuildEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IconHash { get; set; }
    public ulong OwnerId { get; set; }
    public DateTime? LeftAtUtc { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public ICollection<ChannelEntity> Channels { get; set; } = [];
    public ICollection<MemberEntity> Members { get; set; } = [];
    public ICollection<RoleEntity> Roles { get; set; } = [];
    public ICollection<EmoteEntity> Emotes { get; set; } = [];
    public ICollection<ActivityEntity> Activities { get; set; } = [];

    // Navigation properties - Events (soft relations)
    public ICollection<MessageEventEntity> MessageEvents { get; set; } = [];
    public ICollection<ReactionEventEntity> ReactionEvents { get; set; } = [];
    public ICollection<VoiceStateEventEntity> VoiceStateEvents { get; set; } = [];
    public ICollection<VoiceServerEventEntity> VoiceServerEvents { get; set; } = [];
    public ICollection<PresenceEventEntity> PresenceEvents { get; set; } = [];
    public ICollection<GuildMembersChunkEventEntity> GuildMembersChunkEvents { get; set; } = [];
    public ICollection<ThreadSyncEventEntity> ThreadSyncEvents { get; set; } = [];
    public ICollection<MemberEventEntity> MemberEvents { get; set; } = [];
    public ICollection<BanEventEntity> BanEvents { get; set; } = [];
    public ICollection<ChannelEventEntity> ChannelEvents { get; set; } = [];
    public ICollection<RoleEventEntity> RoleEvents { get; set; } = [];
    public ICollection<ThreadEventEntity> ThreadEvents { get; set; } = [];
    public ICollection<GuildEventEntity> GuildEvents { get; set; } = [];
    public ICollection<EmojiEventEntity> EmojiEvents { get; set; } = [];
    public ICollection<StickerEventEntity> StickerEvents { get; set; } = [];
    public ICollection<StageInstanceEventEntity> StageInstanceEvents { get; set; } = [];
    public ICollection<PollEventEntity> PollEvents { get; set; } = [];
    public ICollection<PinEventEntity> PinEvents { get; set; } = [];
    public ICollection<WebhookEventEntity> WebhookEvents { get; set; } = [];
    public ICollection<IntegrationEventEntity> IntegrationEvents { get; set; } = [];
    public ICollection<AutoModEventEntity> AutoModEvents { get; set; } = [];
    public ICollection<AutoModRuleEventEntity> AutoModRuleEvents { get; set; } = [];
    public ICollection<ScheduledEventEntity> ScheduledEvents { get; set; } = [];
    public ICollection<InviteEventEntity> InviteEvents { get; set; } = [];
    public ICollection<TypingEventEntity> TypingEvents { get; set; } = [];
    public ICollection<AuditLogEventEntity> AuditLogEvents { get; set; } = [];
}
