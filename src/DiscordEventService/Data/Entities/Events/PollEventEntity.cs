namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
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
