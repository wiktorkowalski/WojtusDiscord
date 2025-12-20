using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class RoleEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Color { get; set; }
    public bool IsHoisted { get; set; }
    public int Position { get; set; }
    public long Permissions { get; set; }
    public bool IsManaged { get; set; }
    public bool IsMentionable { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;

    // Navigation properties - Events (soft relations)
    public ICollection<RoleEventEntity> RoleEvents { get; set; } = [];
}
