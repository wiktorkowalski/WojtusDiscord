using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Data.Entities.Events;

/// <summary>
/// Dead-letter table for Discord events that failed to process.
/// Allows investigation and potential replay of failed events.
/// </summary>
public class FailedEventEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The type of event that failed (e.g., "MessageCreated", "VoiceStateUpdated")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The handler class that failed to process the event
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string HandlerName { get; set; } = string.Empty;

    /// <summary>
    /// Guild ID if applicable
    /// </summary>
    public ulong? GuildDiscordId { get; set; }

    /// <summary>
    /// Channel ID if applicable
    /// </summary>
    public ulong? ChannelDiscordId { get; set; }

    /// <summary>
    /// User ID if applicable
    /// </summary>
    public ulong? UserDiscordId { get; set; }

    /// <summary>
    /// The serialized event args for potential replay
    /// </summary>
    public string? EventJson { get; set; }

    /// <summary>
    /// The exception type that caused the failure
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string ExceptionType { get; set; } = string.Empty;

    /// <summary>
    /// The exception message
    /// </summary>
    [Required]
    public string ExceptionMessage { get; set; } = string.Empty;

    /// <summary>
    /// Full stack trace for debugging
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Number of times this event has been retried
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the failure occurred
    /// </summary>
    public DateTime FailedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the event was originally received
    /// </summary>
    public DateTime EventReceivedAtUtc { get; set; }

    /// <summary>
    /// Whether this failed event has been acknowledged/resolved
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// When the event was resolved (if applicable)
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>
    /// Notes about resolution
    /// </summary>
    public string? ResolutionNotes { get; set; }
}
