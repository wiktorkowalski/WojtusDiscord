using DiscordEventService.Data;

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
}
