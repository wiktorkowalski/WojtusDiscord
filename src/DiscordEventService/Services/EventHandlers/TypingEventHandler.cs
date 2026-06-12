using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class TypingEventHandler(EventPipeline pipeline, IMemoryCache cache) :
    IEventHandler<TypingStartedEventArgs>
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(10);

    public async Task HandleEventAsync(DiscordClient sender, TypingStartedEventArgs e)
    {
        var cacheKey = $"typing:{e.User.Id}:{e.Channel.Id}";
        if (cache.TryGetValue(cacheKey, out _))
            return;
        cache.Set(cacheKey, true, ThrottleWindow);

        await pipeline.ExecuteAsync(e, "TypingStarted", nameof(TypingEventHandler),
            e.Guild?.Id ?? 0, e.Channel.Id, e.User.Id, async ctx =>
            {
                var entity = new TypingEventEntity
                {
                    UserDiscordId = e.User.Id,
                    ChannelDiscordId = e.Channel.Id,
                    GuildDiscordId = e.Guild?.Id,
                    StartedAtUtc = e.StartedAt.UtcDateTime,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                ctx.Db.TypingEvents.Add(entity);
                await ctx.Db.SaveChangesAsync();
            });
    }
}
