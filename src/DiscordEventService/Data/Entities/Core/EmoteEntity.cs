using DiscordEventService.Data;

namespace DiscordEventService.Data.Entities.Core;

public class EmoteEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid? GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsAnimated { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public GuildEntity? Guild { get; set; }
}
