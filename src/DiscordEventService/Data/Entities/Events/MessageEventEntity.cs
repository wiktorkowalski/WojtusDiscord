using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum MessageEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    BulkDeleted = 3
}

public class MessageEventEntity
{
    public Guid Id { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? AuthorId { get; set; }
    public Guid? GuildId { get; set; }
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

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
    public UserEntity? Author { get; set; }
    public MessageEntity? Message { get; set; }
}
