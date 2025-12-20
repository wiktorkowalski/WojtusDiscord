using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public class WebhookEventEntity
{
    public Guid Id { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? ChannelId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
}
