namespace DiscordEventService.Services;

public class HeartbeatBackgroundService(
    IServiceScopeFactory scopeFactory,
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
                using var scope = scopeFactory.CreateScope();
                var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
                await tracker.RecordHeartbeatAsync(DateTime.UtcNow);
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
}
