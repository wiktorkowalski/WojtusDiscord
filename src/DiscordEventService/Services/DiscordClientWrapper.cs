using DSharpPlus;

namespace DiscordEventService.Services;

/// <summary>
/// Wrapper around DiscordClient that handles disposal safely.
/// DSharpPlus throws NullReferenceException in Dispose() if never connected.
/// </summary>
public class DiscordClientWrapper(DiscordClient client, ILogger<DiscordClientWrapper> logger) : IAsyncDisposable
{
    public DiscordClient Client => client;
    public bool WasConnected { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (!WasConnected)
        {
            logger.LogDebug("DiscordClient was never connected, skipping disposal");
            return;
        }

        try
        {
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during Discord disconnect");
        }

        try
        {
            if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error disposing DiscordClient");
        }
    }
}
