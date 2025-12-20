namespace DiscordEventService.Data.Entities.Core;

public class WebhookEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid GuildId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? CreatorId { get; set; }
    public string? Name { get; set; }
    public string? AvatarHash { get; set; }
    public int Type { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public GuildEntity Guild { get; set; } = null!;
    public ChannelEntity Channel { get; set; } = null!;
    public UserEntity? Creator { get; set; }
}
