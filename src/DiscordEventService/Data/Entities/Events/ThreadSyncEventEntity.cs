namespace DiscordEventService.Data.Entities.Events;

public class ThreadSyncEventEntity
{
    public Guid Id { get; set; }

    public ulong GuildDiscordId { get; set; }

    public int ThreadCount { get; set; }

    public string? ThreadIdsJson { get; set; }

    public string? ChannelIdsJson { get; set; }

    public string? MembersJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
