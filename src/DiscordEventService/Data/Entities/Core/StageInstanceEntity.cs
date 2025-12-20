using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class StageInstanceEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid GuildId { get; set; }
    public Guid ChannelId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public int PrivacyLevel { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;
    public ChannelEntity Channel { get; set; } = null!;

    // Navigation properties - Events (soft relations)
    public ICollection<StageInstanceEventEntity> StageInstanceEvents { get; set; } = [];
}
