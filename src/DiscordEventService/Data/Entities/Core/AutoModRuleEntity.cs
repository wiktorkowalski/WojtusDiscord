namespace DiscordEventService.Data.Entities.Core;

public class AutoModRuleEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? CreatorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int EventType { get; set; }
    public int TriggerType { get; set; }
    public string? TriggerMetadataJson { get; set; }
    public string? ActionsJson { get; set; }
    public bool IsEnabled { get; set; }
    public string? ExemptRolesJson { get; set; }
    public string? ExemptChannelsJson { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity? Guild { get; set; }
    public UserEntity? Creator { get; set; }
}
