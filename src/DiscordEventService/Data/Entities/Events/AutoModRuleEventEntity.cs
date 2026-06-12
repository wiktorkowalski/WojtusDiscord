namespace DiscordEventService.Data.Entities.Events;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum AutoModRuleEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class AutoModRuleEventEntity
{
    public Guid Id { get; set; }
    public ulong RuleDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong CreatorDiscordId { get; set; }
    public AutoModRuleEventType EventType { get; set; }

    public string? Name { get; set; }
    public int? TriggerType { get; set; }
    public bool? IsEnabled { get; set; }
    public string? ActionsJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
