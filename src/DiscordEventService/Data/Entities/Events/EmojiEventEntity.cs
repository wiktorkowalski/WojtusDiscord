namespace DiscordEventService.Data.Entities.Events;

public class EmojiEventEntity
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }

    public string? EmojisAddedJson { get; set; }
    public string? EmojisRemovedJson { get; set; }
    public string? EmojisUpdatedJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
