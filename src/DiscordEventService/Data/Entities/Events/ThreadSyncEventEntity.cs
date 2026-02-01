using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public class ThreadSyncEventEntity
{
    public Guid Id { get; set; }

    public Guid? GuildId { get; set; }
    public ulong GuildDiscordId { get; set; }

    // Number of threads synced
    public int ThreadCount { get; set; }

    // JSON array of thread IDs that were synced
    public string? ThreadIdsJson { get; set; }

    // JSON array of channel IDs whose threads are being synced
    public string? ChannelIdsJson { get; set; }

    // JSON array of thread member objects
    public string? MembersJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation property
    public GuildEntity? Guild { get; set; }
}
