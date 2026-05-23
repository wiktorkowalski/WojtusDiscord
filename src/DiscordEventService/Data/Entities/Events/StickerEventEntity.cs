namespace DiscordEventService.Data.Entities.Events;

public class StickerEventEntity
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }

    public string? StickersAddedJson { get; set; }
    public string? StickersRemovedJson { get; set; }
    public string? StickersUpdatedJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }
}
