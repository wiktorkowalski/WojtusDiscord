namespace DiscordEventService.Data.Entities.Events;

public class AuditLogEventEntity
{
    public Guid Id { get; set; }
    public ulong AuditLogDiscordId { get; set; }
    public ulong GuildDiscordId { get; set; }
    public ulong? UserDiscordId { get; set; }
    public ulong? TargetDiscordId { get; set; }
    public int ActionType { get; set; }
    public string? Reason { get; set; }
    public string? ChangesJson { get; set; }
    public string? OptionsJson { get; set; }

    public DateTime EventTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public string? RawEventJson { get; set; }
}
