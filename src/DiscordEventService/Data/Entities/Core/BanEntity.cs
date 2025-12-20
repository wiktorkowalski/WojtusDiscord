namespace DiscordEventService.Data.Entities.Core;

public class BanEntity : ITimestamped
{
    public Guid Id { get; set; }
    public Guid GuildId { get; set; }
    public Guid UserId { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; }
    public DateTime BannedAtUtc { get; set; }
    public DateTime? UnbannedAtUtc { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public GuildEntity Guild { get; set; } = null!;
    public UserEntity User { get; set; } = null!;
}
