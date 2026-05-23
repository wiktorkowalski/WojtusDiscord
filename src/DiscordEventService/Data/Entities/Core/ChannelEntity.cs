using DiscordEventService.Data;

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
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;
}

public enum ChannelType
{
    // Sentinel for Discord-side types we don't yet model. Direct casts
    // ((ChannelType)channel.Type) preserve the raw int regardless of whether
    // it's a known value, but downstream switches and mappers need a name to
    // route on. Anything mapped via MapChannelType that isn't recognized
    // becomes Unknown and logs a warning so drift is visible. See §P2.2 / #71.
    Unknown = -1,
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
