namespace DiscordEventService.Data.Entities.Core;

public class StickerEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid? GuildId { get; set; }
    public ulong? PackId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public int Type { get; set; }
    public int FormatType { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties
    public GuildEntity? Guild { get; set; }
}
