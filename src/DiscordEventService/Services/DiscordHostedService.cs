using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using DSharpPlus.Exceptions;

namespace DiscordEventService.Services;

internal sealed class DiscordHostedService(
    DiscordClient client,
    IServiceScopeFactory scopeFactory,
    ILogger<DiscordHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve prior-session downtime BEFORE connecting. Once ConnectAsync
        // returns, DSharpPlus begins dispatching events that accumulated during
        // the gap; their handlers write raw_event_logs rows with
        // ReceivedAtUtc = now, which would pollute InferStartupGapAsync's
        // maxReceivedAt query and silently mask the real gap.
        try
        {
            using var scope = scopeFactory.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
            var closed = await tracker.CloseOpenDowntimeAsync(DateTime.UtcNow);
            if (closed == 0)
                await tracker.InferStartupGapAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Downtime tracker failed during StartAsync");
        }

        logger.LogInformation("Connecting to Discord...");
        try
        {
            await client.ConnectAsync();
            logger.LogInformation("Connected to Discord as {Username}", client.CurrentUser?.Username ?? "Unknown");
        }
        catch (UnauthorizedException ex)
        {
            logger.LogCritical(ex, "Invalid Discord bot token - check Discord:Token configuration. Service will continue without Discord connection.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to connect to Discord. Service will continue without Discord connection.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
            var isDeploy = Environment.GetEnvironmentVariable("DEPLOY_IN_PROGRESS") == "1";
            var deployVersion = Environment.GetEnvironmentVariable("DEPLOY_VERSION");
            await tracker.OpenDowntimeAsync(
                isDeploy ? BotDowntimeType.Deploy : BotDowntimeType.GracefulShutdown,
                BotDowntimeDetectionMethod.GracefulStop,
                isDeploy && !string.IsNullOrEmpty(deployVersion) ? $"deploy: {deployVersion}" : null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Downtime tracker failed during StopAsync");
        }

        logger.LogInformation("Disconnecting from Discord...");
        try
        {
            await client.DisconnectAsync();
            logger.LogInformation("Disconnected from Discord successfully");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during Discord disconnect");
        }
    }
}
