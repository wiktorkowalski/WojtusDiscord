using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class GuildScheduledEventEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid GuildId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? CreatorId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Status { get; set; }
    public int EntityType { get; set; }

    public DateTime ScheduledStartTimeUtc { get; set; }
    public DateTime? ScheduledEndTimeUtc { get; set; }

    public string? EntityMetadataLocation { get; set; }
    public int UserCount { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;
    public ChannelEntity? Channel { get; set; }
    public UserEntity? Creator { get; set; }

    // Navigation properties - Events (soft relations)
    public ICollection<ScheduledEventEntity> ScheduledEvents { get; set; } = [];
}
