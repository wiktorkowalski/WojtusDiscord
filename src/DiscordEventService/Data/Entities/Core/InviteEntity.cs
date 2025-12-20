namespace DiscordEventService.Data.Entities.Core;

public class InviteEntity : ITimestamped
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid GuildId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? InviterId { get; set; }

    public int MaxAge { get; set; }
    public int MaxUses { get; set; }
    public int Uses { get; set; }
    public bool IsTemporary { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public GuildEntity Guild { get; set; } = null!;
    public ChannelEntity Channel { get; set; } = null!;
    public UserEntity? Inviter { get; set; }
}
