namespace DiscordEventService.Data.Entities.Core;

public class VoiceStateEntity : ITimestamped
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid GuildId { get; set; }

    public Guid? ChannelId { get; set; }
    public string? SessionId { get; set; }

    public bool IsSelfMuted { get; set; }
    public bool IsSelfDeafened { get; set; }
    public bool IsServerMuted { get; set; }
    public bool IsServerDeafened { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsVideo { get; set; }
    public bool IsSuppressed { get; set; }

    public DateTime? JoinedChannelAtUtc { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public UserEntity User { get; set; } = null!;
    public GuildEntity Guild { get; set; } = null!;
    public ChannelEntity? Channel { get; set; }
}
