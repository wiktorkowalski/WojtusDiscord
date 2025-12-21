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
            logger.LogCritical(ex, "Invalid Discord bot token - check Discord:Token configuration");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to connect to Discord");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disconnecting from Discord...");
        await client.DisconnectAsync();

        if (client is IDisposable disposable)
        {
            disposable.Dispose();
            logger.LogDebug("DiscordClient disposed");
        }

        logger.LogInformation("Disconnected from Discord");
    }
}
