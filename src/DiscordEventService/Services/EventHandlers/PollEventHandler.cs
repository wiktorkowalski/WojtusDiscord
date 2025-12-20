using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class PollEventHandler(IServiceScopeFactory scopeFactory, ILogger<PollEventHandler> logger) :
    IEventHandler<MessagePollVotedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessagePollVotedEventArgs e)
    {
        var update = e.PollVoteUpdate;
        if (update.Guild is null) return;

        try
        {
            var now = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessagePollVoted", update.Guild.Id, update.Message?.ChannelId, update.User?.Id);

            db.PollEvents.Add(new PollEventEntity
            {
                MessageDiscordId = update.Message?.Id ?? 0,
                ChannelDiscordId = update.Message?.ChannelId ?? 0,
                GuildDiscordId = update.Guild.Id,
                UserDiscordId = update.User?.Id ?? 0,
                AnswerId = 0, // AnswerId not exposed in this event
                EventType = update.WasAdded ? PollEventType.VoteAdded : PollEventType.VoteRemoved,
                EventTimestampUtc = now,
                ReceivedAtUtc = now,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling poll vote");
        }
    }
}
