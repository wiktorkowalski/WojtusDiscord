namespace DiscordEventService.Data.Entities.Events;

public enum PollEventType
{
    VoteAdded = 0,
    VoteRemoved = 1
}

public class PollEventEntity
{
    public Guid Id { get; set; }
    public ulong MessageDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong UserDiscordId { get; set; }
    public int AnswerId { get; set; }
    public PollEventType EventType { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
