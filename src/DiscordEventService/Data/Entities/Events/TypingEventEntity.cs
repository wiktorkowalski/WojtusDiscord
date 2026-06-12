namespace DiscordEventService.Data.Entities.Events;

public class TypingEventEntity
{
    public Guid Id { get; set; }
    public ulong UserDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong? GuildDiscordId { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    // Raw serialized event args from DSharpPlus for debugging
    public string? RawEventJson { get; set; }
}
