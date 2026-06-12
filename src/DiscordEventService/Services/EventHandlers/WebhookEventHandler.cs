using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class WebhookEventHandler(EventPipeline pipeline) :
    IEventHandler<WebhooksUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, WebhooksUpdatedEventArgs e)
    {
        await pipeline.Execute(e, "WebhooksUpdated", nameof(WebhookEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                ctx.Db.WebhookEvents.Add(new WebhookEventEntity
                {
                    GuildDiscordId = e.Guild.Id,
                    ChannelDiscordId = e.Channel.Id,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
