namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum MessageEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    BulkDeleted = 3,
    Backfilled = 4
}

public class MessageEventEntity
{
    public Guid Id { get; set; }
    public ulong MessageDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong? AuthorDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public MessageEventType EventType { get; set; }
    public string? Content { get; set; }
    public string? ContentBefore { get; set; }
    public bool HasAttachments { get; set; }
    public bool HasEmbeds { get; set; }
    public ulong? ReplyToMessageDiscordId { get; set; }
    public string? AttachmentsJson { get; set; }
    public string? EmbedsJson { get; set; }
    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
