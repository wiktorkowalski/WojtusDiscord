namespace DiscordEventService.Data.Entities.Events;

public class PinEventEntity
{
    public Guid Id { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public DateTime? LastPinTimestampUtc { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
