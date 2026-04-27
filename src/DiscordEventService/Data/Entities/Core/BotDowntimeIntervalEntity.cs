namespace DiscordEventService.Data.Entities.Core;

public class BotDowntimeIntervalEntity : ITimestamped
{
    public Guid Id { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public BotDowntimeType Type { get; set; }
    public BotDowntimeDetectionMethod DetectionMethod { get; set; }
    public DateTime? LastEventBeforeUtc { get; set; }
    public DateTime? FirstEventAfterUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}

public enum BotDowntimeType
{
    GracefulShutdown = 0,
    Deploy = 1,
    HostDown = 2,
    GatewayDisconnect = 3,
    DbUnreachable = 4,
    Inferred = 5
}

public enum BotDowntimeDetectionMethod
{
    GracefulStop = 0,
    StartupGapInference = 1,
    GatewayEvent = 2,
    Manual = 3
}
