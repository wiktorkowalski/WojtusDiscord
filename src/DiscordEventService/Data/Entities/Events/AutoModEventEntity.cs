namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum AutoModEventType
{
    RuleCreated = 0,
    RuleUpdated = 1,
    RuleDeleted = 2,
    ActionExecuted = 3
}

public class AutoModEventEntity
{
    public Guid Id { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong RuleDiscordId { get; set; }
    public AutoModEventType EventType { get; set; }

    // Rule details
    public string? RuleName { get; set; }
    public int? TriggerType { get; set; }

    // Action execution details
    public ulong? UserDiscordId { get; set; }
    public ulong? ChannelDiscordId { get; set; }
    public ulong? MessageDiscordId { get; set; }
    public ulong? AlertSystemMessageDiscordId { get; set; }
    public string? Content { get; set; }
    public string? MatchedKeyword { get; set; }
    public string? MatchedContent { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
