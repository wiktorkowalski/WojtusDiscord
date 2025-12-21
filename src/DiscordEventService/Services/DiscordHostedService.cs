using DSharpPlus.Exceptions;

namespace DiscordEventService.Services;

public class DiscordHostedService(
    DiscordClientWrapper clientWrapper,
    ILogger<DiscordHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Connecting to Discord...");
        try
        {
            await clientWrapper.Client.ConnectAsync();
            clientWrapper.WasConnected = true;
            logger.LogInformation("Connected to Discord as {Username}", clientWrapper.Client.CurrentUser?.Username ?? "Unknown");
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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Disposal is handled by DiscordClientWrapper.DisposeAsync()
        logger.LogInformation("Discord hosted service stopping");
        return Task.CompletedTask;
    }
}
