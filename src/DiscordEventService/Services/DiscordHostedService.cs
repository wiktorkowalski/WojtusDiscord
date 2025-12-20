using DSharpPlus;

namespace DiscordEventService.Services;

public class DiscordHostedService(
    DiscordClient client,
    ILogger<DiscordHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Connecting to Discord...");
        await client.ConnectAsync();
        logger.LogInformation("Connected to Discord as {Username}", client.CurrentUser?.Username ?? "Unknown");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disconnecting from Discord...");
        await client.DisconnectAsync();
        logger.LogInformation("Disconnected from Discord");
    }
}
