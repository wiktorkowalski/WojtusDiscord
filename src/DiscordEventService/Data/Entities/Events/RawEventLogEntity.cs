using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Data.Entities.Events;

/// <summary>
/// Stores raw JSON of all Discord events for debugging and future data extraction.
/// </summary>
public class RawEventLogEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The type of event (e.g., "MessageCreated", "PresenceUpdated", "VoiceStateUpdated")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Guild ID if applicable (0 for DM events)
    /// </summary>
    public ulong GuildDiscordId { get; set; }

    /// <summary>
    /// Channel ID if applicable
    /// </summary>
    public ulong? ChannelDiscordId { get; set; }

    /// <summary>
    /// User ID if applicable (the user who triggered the event)
    /// </summary>
    public ulong? UserDiscordId { get; set; }

    /// <summary>
    /// The full serialized event args object from DSharpPlus
    /// </summary>
    [Required]
    public string EventJson { get; set; } = string.Empty;

    /// <summary>
    /// Size of the JSON in bytes (for monitoring storage)
    /// </summary>
    public int JsonSizeBytes { get; set; }

    /// <summary>
    /// When the event was received
    /// </summary>
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
