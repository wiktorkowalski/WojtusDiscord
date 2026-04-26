using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class WebhookEventHandler(IServiceScopeFactory scopeFactory, ILogger<WebhookEventHandler> logger) :
    IEventHandler<WebhooksUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, WebhooksUpdatedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "WebhooksUpdated", e.Guild.Id, e.Channel.Id, null);

            db.WebhookEvents.Add(new WebhookEventEntity
            {
                GuildDiscordId = e.Guild.Id,
                ChannelDiscordId = e.Channel.Id,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling webhooks updated for ChannelId={ChannelId}", e.Channel.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "WebhooksUpdated", nameof(WebhookEventHandler), ex,
                e.Guild?.Id, e.Channel?.Id, null, rawJson);
        }
    }
}
