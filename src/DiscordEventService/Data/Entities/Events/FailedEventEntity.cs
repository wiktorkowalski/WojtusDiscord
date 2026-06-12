
namespace DiscordEventService.Data.Entities.Events;

public class FailedEventEntity
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string HandlerName { get; set; } = string.Empty;

    public ulong? GuildDiscordId { get; set; }

    public ulong? ChannelDiscordId { get; set; }

    public ulong? UserDiscordId { get; set; }

    // Serialized event args retained for potential replay.
    public string? EventJson { get; set; }

    public string ExceptionType { get; set; } = string.Empty;

    public string ExceptionMessage { get; set; } = string.Empty;

    public string? StackTrace { get; set; }

    public int RetryCount { get; set; }

    public DateTime FailedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime EventReceivedAtUtc { get; set; }

    public bool IsResolved { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public string? ResolutionNotes { get; set; }

    public Guid? CorrelationId { get; set; }
}
