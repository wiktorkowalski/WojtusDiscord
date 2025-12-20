using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Data.Entities.Events;

public enum IntegrationEventType
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

public class IntegrationEventEntity
{
    public Guid Id { get; set; }
    public Guid? IntegrationId { get; set; }
    public Guid? GuildId { get; set; }
    public ulong IntegrationDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public IntegrationEventType EventType { get; set; }

    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool? IsEnabled { get; set; }
    public ulong? ApplicationDiscordId { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Raw serialized event args from DSharpPlus for debugging</summary>
    public string? RawEventJson { get; set; }

    // Navigation properties (soft relations - no FK constraint)
    public GuildEntity? Guild { get; set; }
    public IntegrationEntity? Integration { get; set; }
}
