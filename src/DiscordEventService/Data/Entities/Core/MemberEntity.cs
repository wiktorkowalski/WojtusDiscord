using DiscordEventService.Data;

namespace DiscordEventService.Data.Entities.Core;

public class MemberEntity : ITimestamped
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid GuildId { get; set; }

    public string? Nickname { get; set; }
    public string? GuildAvatarHash { get; set; }
    public DateTime? JoinedAtUtc { get; set; }
    public DateTime? PremiumSinceUtc { get; set; }
    public bool IsDeafened { get; set; }
    public bool IsMuted { get; set; }
    public bool IsPending { get; set; }
    public DateTime? TimeoutUntilUtc { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public UserEntity User { get; set; } = null!;
    public GuildEntity Guild { get; set; } = null!;
}
