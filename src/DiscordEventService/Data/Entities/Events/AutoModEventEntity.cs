using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

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
    public Guid? GuildId { get; set; }
    public Guid? RuleId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong RuleDiscordId { get; set; }
    public AutoModEventType EventType { get; set; }

    // Rule details
    public string? RuleName { get; set; }
    public int? TriggerType { get; set; }

    // Action execution details
    public Guid? UserId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? MessageId { get; set; }
    public ulong? UserDiscordId { get; set; }
    public ulong? ChannelDiscordId { get; set; }
    public ulong? MessageDiscordId { get; set; }
    public ulong? AlertSystemMessageDiscordId { get; set; }
    public string? Content { get; set; }
    public string? MatchedKeyword { get; set; }
    public string? MatchedContent { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public ChannelEntity? Channel { get; set; }
    public UserEntity? User { get; set; }
    public AutoModRuleEntity? Rule { get; set; }
}
