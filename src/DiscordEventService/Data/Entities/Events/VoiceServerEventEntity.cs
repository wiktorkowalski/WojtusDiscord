using System.ComponentModel.DataAnnotations;
using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public class VoiceServerEventEntity
{
    public Guid Id { get; set; }

    public Guid? GuildId { get; set; }
    public ulong GuildDiscordId { get; set; }

    [MaxLength(256)]
    public string? Endpoint { get; set; }

    // Note: We intentionally do NOT store the voice server token for security reasons

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation property
    public GuildEntity? Guild { get; set; }
}
