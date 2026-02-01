namespace DiscordEventService.Data.Entities.Core;

public class MessageEditHistoryEntity
{
    public Guid Id { get; set; }

    public Guid? MessageId { get; set; }
    public MessageEntity? Message { get; set; }

    public ulong MessageDiscordId { get; set; }

    public string? ContentBefore { get; set; }

    public string? ContentAfter { get; set; }

    public DateTime EditedAtUtc { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
