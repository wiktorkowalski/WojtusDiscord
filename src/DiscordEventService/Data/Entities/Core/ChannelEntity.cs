using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class ChannelEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid GuildId { get; set; }
    public ulong? ParentDiscordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
    public string? Topic { get; set; }
    public int? Bitrate { get; set; }
    public int? UserLimit { get; set; }
    public int? RateLimitPerUser { get; set; }
    public bool IsNsfw { get; set; }
    public int Position { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;

    // Navigation properties - Events (soft relations)
    public ICollection<MessageEventEntity> MessageEvents { get; set; } = [];
    public ICollection<ReactionEventEntity> ReactionEvents { get; set; } = [];
    public ICollection<ChannelEventEntity> ChannelEvents { get; set; } = [];
    public ICollection<PollEventEntity> PollEvents { get; set; } = [];
    public ICollection<PinEventEntity> PinEvents { get; set; } = [];
    public ICollection<WebhookEventEntity> WebhookEvents { get; set; } = [];
    public ICollection<StageInstanceEventEntity> StageInstanceEvents { get; set; } = [];
    public ICollection<AutoModEventEntity> AutoModEvents { get; set; } = [];
    public ICollection<InviteEventEntity> InviteEvents { get; set; } = [];
    public ICollection<TypingEventEntity> TypingEvents { get; set; } = [];
    public ICollection<ScheduledEventEntity> ScheduledEvents { get; set; } = [];
}

public enum ChannelType
{
    Text = 0,
    Private = 1,
    Voice = 2,
    Group = 3,
    Category = 4,
    News = 5,
    Store = 6,
    NewsThread = 10,
    PublicThread = 11,
    PrivateThread = 12,
    Stage = 13,
    GuildDirectory = 14,
    Forum = 15,
    Media = 16
}
