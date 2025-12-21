using DSharpPlus;
using DSharpPlus.Exceptions;

namespace DiscordEventService.Services;

public class DiscordHostedService(
    DiscordClient client,
    ILogger<DiscordHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
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
        logger.LogInformation("Discord hosted service stopping");
        try
        {
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during Discord disconnect");
        }
    }
}
