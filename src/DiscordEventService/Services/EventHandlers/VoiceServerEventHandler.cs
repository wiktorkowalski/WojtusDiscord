using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public sealed class VoiceServerEventHandler(EventPipeline pipeline) :
    IEventHandler<VoiceServerUpdatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, VoiceServerUpdatedEventArgs args)
    {
        await pipeline.Execute(args, "VoiceServerUpdated", nameof(VoiceServerEventHandler),
            args.Guild.Id, null, null, async ctx =>
            {
                ctx.Db.VoiceServerEvents.Add(new VoiceServerEventEntity
                {
                    GuildDiscordId = args.Guild.Id,
                    Endpoint = args.Endpoint,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();

                ctx.Logger.LogDebug("Recorded voice server event for guild {GuildId}, endpoint {Endpoint}",
                    args.Guild.Id, args.Endpoint);
            });
    }
}
