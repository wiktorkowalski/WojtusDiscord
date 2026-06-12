namespace DiscordEventService.Data.Entities.Events;

public class VoiceServerEventEntity
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }

    public string? Endpoint { get; set; }

    // Note: We intentionally do NOT store the voice server token for security reasons

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    // Raw serialized event args from DSharpPlus for debugging.
    public string? RawEventJson { get; set; }
}
