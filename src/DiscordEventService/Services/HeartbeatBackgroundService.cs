using DSharpPlus;

namespace DiscordEventService.Services;

public class HeartbeatBackgroundService(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<HeartbeatBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (isConnected, latencyMs) = ReadGatewayState();
                using var scope = scopeFactory.CreateScope();
                var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
                await tracker.RecordHeartbeatAsync(DateTime.UtcNow, isConnected, latencyMs);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat write failed; will retry next tick");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private (bool? IsConnected, int? LatencyMs) ReadGatewayState()
    {
        // DiscordClient.AllShardsConnected is the direct gateway-up signal; the
        // per-guild latency is informational. Both are wrapped in try/catch
        // because DSharpPlus can briefly throw during reconnect handshakes.
        bool? isConnected = null;
        int? latencyMs = null;

        try
        {
            isConnected = discordClient.AllShardsConnected;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "AllShardsConnected lookup failed");
        }

        try
        {
            var firstGuildId = discordClient.Guilds.Keys.FirstOrDefault();
            if (firstGuildId != 0)
            {
                latencyMs = (int)discordClient.GetConnectionLatency(firstGuildId).TotalMilliseconds;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "GetConnectionLatency lookup failed");
        }

        return (isConnected, latencyMs);
    }
}
