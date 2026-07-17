using DiscordEventService.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// Delivery seam for §3 cost-cap alerts (#269): UsageAlertService decides WHAT to say,
// this decides WHERE it goes. An interface so the threshold/dedup logic tests against
// a recording fake — the Discord impl is live-verified only, like the renderer.
internal interface IUsageAlertNotifier
{
    Task NotifyAdminsAsync(string message, CancellationToken cancellationToken);
}

// DMs every configured admin (#269 transport decision: Discord DM, not ntfy/Grafana —
// no homelab coupling to survive the Hetzner move). Per-admin failures are logged and
// swallowed: one refused DM must not stop the remaining admins, and an alert-path
// failure must never break the conversation turn.
internal sealed class DiscordUsageAlertNotifier(
    DiscordClientAccessor clientAccessor,
    IOptions<ConversationOptions> conversationOptions,
    ILogger<DiscordUsageAlertNotifier> logger) : IUsageAlertNotifier
{
    public async Task NotifyAdminsAsync(string message, CancellationToken cancellationToken)
    {
        var adminIds = conversationOptions.Value.AdminUserIds;
        if (adminIds.Length == 0)
        {
            logger.LogWarning("Cost alert had no AdminUserIds to DM; dropping: {Message}", message);
            return;
        }

        foreach (var adminId in adminIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var user = await clientAccessor.Client.GetUserAsync(adminId);
                var dm = await user.CreateDmChannelAsync();
                await dm.SendMessageAsync(message);
                logger.LogInformation("Cost alert DM sent to admin {AdminId}", adminId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to DM cost alert to admin {AdminId}", adminId);
            }
        }
    }
}
