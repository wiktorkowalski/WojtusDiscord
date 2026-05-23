namespace DiscordEventService.Data.Entities.Core;

public class MessageEditHistoryEntity
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }
    public MessageEntity Message { get; set; } = null!;

    public ulong MessageDiscordId { get; set; }

    public string? ContentBefore { get; set; }
    public string? ContentAfter { get; set; }

    public string? AttachmentsBeforeJson { get; set; }
    public string? AttachmentsAfterJson { get; set; }

    public string? EmbedsBeforeJson { get; set; }
    public string? EmbedsAfterJson { get; set; }

    public int? FlagsBefore { get; set; }
    public int? FlagsAfter { get; set; }

    public DateTime EditedAtUtc { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
