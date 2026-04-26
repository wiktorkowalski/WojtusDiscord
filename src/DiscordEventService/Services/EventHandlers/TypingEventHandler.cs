using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services.EventHandlers;

public class TypingEventHandler(IServiceScopeFactory scopeFactory, ILogger<TypingEventHandler> logger, IMemoryCache cache) :
    IEventHandler<TypingStartedEventArgs>
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(10);

    public async Task HandleEventAsync(DiscordClient sender, TypingStartedEventArgs e)
    {
        string? rawJson = null;
        try
        {
            // Throttle: skip if we've seen this user+channel combo recently
            var cacheKey = $"typing:{e.User.Id}:{e.Channel.Id}";
            if (cache.TryGetValue(cacheKey, out _))
            {
                return;
            }
            cache.Set(cacheKey, true, ThrottleWindow);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "TypingStarted", e.Guild?.Id ?? 0, e.Channel.Id, e.User.Id);

            var entity = new TypingEventEntity
            {
                UserDiscordId = e.User.Id,
                ChannelDiscordId = e.Channel.Id,
                GuildDiscordId = e.Guild?.Id,
                StartedAt = e.StartedAt.UtcDateTime,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            db.TypingEvents.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling typing started");
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "TypingStarted", nameof(TypingEventHandler), ex,
                e.Guild?.Id, e.Channel?.Id, e.User?.Id, rawJson);
        }
    }
}
