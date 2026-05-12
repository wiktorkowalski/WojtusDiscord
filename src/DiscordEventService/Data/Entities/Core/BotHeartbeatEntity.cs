namespace DiscordEventService.Data.Entities.Core;

public class BotHeartbeatEntity : ITimestamped
{
    public Guid Id { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public bool? IsGatewayConnected { get; set; }
    public int? GatewayLatencyMs { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
