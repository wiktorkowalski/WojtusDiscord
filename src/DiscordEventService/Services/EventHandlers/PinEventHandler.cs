using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class PinEventHandler(IServiceScopeFactory scopeFactory, ILogger<PinEventHandler> logger) :
    IEventHandler<ChannelPinsUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ChannelPinsUpdatedEventArgs e)
    {
        if (e.Guild is null) return;

        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "ChannelPinsUpdated", e.Guild.Id, e.Channel.Id, null);

            db.PinEvents.Add(new PinEventEntity
            {
                ChannelDiscordId = e.Channel.Id,
                GuildDiscordId = e.Guild.Id,
                LastPinTimestampUtc = e.LastPinTimestamp?.UtcDateTime,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling pins updated for ChannelId={ChannelId}", e.Channel.Id);
        }
    }
}
