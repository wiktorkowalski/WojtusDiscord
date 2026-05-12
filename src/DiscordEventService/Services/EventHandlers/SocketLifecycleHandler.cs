using DiscordEventService.Data.Entities.Core;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class SocketLifecycleHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<SocketLifecycleHandler> logger) :
    IEventHandler<SocketClosedEventArgs>,
    IEventHandler<SessionResumedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, SocketClosedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
            await tracker.OpenDowntimeAsync(
                BotDowntimeType.GatewayDisconnect,
                BotDowntimeDetectionMethod.GatewayEvent,
                $"socket closed: code={e.CloseCode} message={e.CloseMessage}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open downtime row on SocketClosed (code={CloseCode})", e.CloseCode);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, SessionResumedEventArgs e)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<DowntimeTrackerService>();
            // Scope to GatewayDisconnect so a routine resume doesn't clobber a
            // manually-opened Deploy/HostDown row from the ops endpoint.
            await tracker.CloseOpenDowntimeAsync(DateTime.UtcNow, onlyType: BotDowntimeType.GatewayDisconnect);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to close downtime row on SessionResumed");
        }
    }
}
