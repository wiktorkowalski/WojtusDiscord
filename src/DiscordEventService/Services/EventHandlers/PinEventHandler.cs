using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public sealed class PinEventHandler(EventPipeline pipeline) :
    IEventHandler<ChannelPinsUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ChannelPinsUpdatedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.Execute(e, "ChannelPinsUpdated", nameof(PinEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                ctx.Db.PinEvents.Add(new PinEventEntity
                {
                    ChannelDiscordId = e.Channel.Id,
                    GuildDiscordId = e.Guild.Id,
                    LastPinTimestampUtc = e.LastPinTimestamp?.UtcDateTime,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson,
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
