using Hangfire;

namespace DiscordEventService.Infrastructure;

// Reads the Hangfire statistics for the gauges on /metrics every 30 s. Off the scrape on
// purpose: the read is a database query, and a scrape that waits on a database that is down
// times out and loses every other metric with it, at the moment they matter most.
internal sealed class HangfireStatisticsService(JobStorage storage, ILogger<HangfireStatisticsService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Logged on the first failure of a streak only, like the heartbeat's gateway lookups.
        var readFailed = false;

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                BotMetrics.SetHangfireStatistics(storage.GetMonitoringApi().GetStatistics());
                readFailed = false;
            }
            catch (Exception ex)
            {
                BotMetrics.SetHangfireStatistics(null);
                if (!readFailed)
                    logger.LogDebug(ex, "Hangfire statistics read failed (further failures suppressed until recovery)");
                readFailed = true;
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
