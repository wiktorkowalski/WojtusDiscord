namespace DiscordEventService.Data.Entities.Events;

public enum ScheduledEventEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    UserAdded = 3,
    UserRemoved = 4,
    Completed = 5
}

public class ScheduledEventEntity
{
    public Guid Id { get; set; }
    public ulong EventDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong? ChannelDiscordId { get; set; }
    public ulong? CreatorDiscordId { get; set; }
    public ScheduledEventEventType EventType { get; set; }

    // Event details
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Status { get; set; }
    public int? EntityType { get; set; }
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }

    // For user add/remove events
    public ulong? UserDiscordId { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    // Raw serialized event args from DSharpPlus for debugging
    public string? RawEventJson { get; set; }
}
