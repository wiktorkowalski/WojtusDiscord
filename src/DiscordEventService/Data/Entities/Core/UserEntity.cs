using DiscordEventService.Data;

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
}
