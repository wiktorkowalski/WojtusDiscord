using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class IntegrationEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsSyncing { get; set; }
    public ulong? RoleDiscordId { get; set; }
    public int ExpireBehavior { get; set; }
    public int ExpireGracePeriod { get; set; }
    public ulong? ApplicationId { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;

    // Navigation properties - Events (soft relations)
    public ICollection<IntegrationEventEntity> IntegrationEvents { get; set; } = [];
}
