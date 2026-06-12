using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class PollEventHandler(EventPipeline pipeline) :
    IEventHandler<MessagePollVotedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessagePollVotedEventArgs e)
    {
        var update = e.PollVoteUpdate;
        if (update.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessagePollVoted", nameof(PollEventHandler),
            update.Guild.Id, update.Message?.ChannelId, update.User?.Id, async ctx =>
            {
                ctx.Db.PollEvents.Add(new PollEventEntity
                {
                    MessageDiscordId = update.Message?.Id ?? 0,
                    ChannelDiscordId = update.Message?.ChannelId ?? 0,
                    GuildDiscordId = update.Guild.Id,
                    UserDiscordId = update.User?.Id ?? 0,
                    AnswerId = 0,
                    EventType = update.WasAdded ? PollEventType.VoteAdded : PollEventType.VoteRemoved,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }
}
