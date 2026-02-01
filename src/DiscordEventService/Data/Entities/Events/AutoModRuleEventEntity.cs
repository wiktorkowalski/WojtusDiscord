using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum AutoModRuleEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class AutoModRuleEventEntity
{
    public Guid Id { get; set; }
    public Guid? RuleId { get; set; }
    public Guid? GuildId { get; set; }
    public Guid? CreatorId { get; set; }
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

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public UserEntity? Creator { get; set; }
    public AutoModRuleEntity? Rule { get; set; }
}
