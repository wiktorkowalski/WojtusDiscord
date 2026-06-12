namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum InviteEventType
{
    Created = 0,
    Deleted = 1
}

public class InviteEventEntity
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong? InviterDiscordId { get; set; }
    public InviteEventType EventType { get; set; }

    public string? Code { get; set; }
    public int? MaxAge { get; set; }
    public int? MaxUses { get; set; }
    public bool IsTemporary { get; set; }
    public int? Uses { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
