
namespace DiscordEventService.Data.Entities.Events;

public class RawEventLogEntity
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public ulong GuildDiscordId { get; set; }

    public ulong? ChannelDiscordId { get; set; }

    public ulong? UserDiscordId { get; set; }

    public string EventJson { get; set; } = string.Empty;

    public int JsonSizeBytes { get; set; }

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? CorrelationId { get; set; }

    // True when EventJson is a diagnostic stub written because the event args could not be
    // serialized — the original payload is unrecoverable, so this row must be excluded from replay.
    public bool IsSerializationFailed { get; set; }
}
