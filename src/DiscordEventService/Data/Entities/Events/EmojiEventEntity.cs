using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public class EmojiEventEntity
{
    public Guid Id { get; set; }
    public Guid? GuildId { get; set; }
    public ulong GuildDiscordId { get; set; }

    public string? EmojisAddedJson { get; set; }
    public string? EmojisRemovedJson { get; set; }
    public string? EmojisUpdatedJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
}
