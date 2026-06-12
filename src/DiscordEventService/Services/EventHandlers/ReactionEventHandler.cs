using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class ReactionEventHandler(EventPipeline pipeline) :
    IEventHandler<MessageReactionAddedEventArgs>,
    IEventHandler<MessageReactionRemovedEventArgs>,
    IEventHandler<MessageReactionsClearedEventArgs>,
    IEventHandler<MessageReactionRemovedEmojiEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessageReactionAddedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessageReactionAdded", nameof(ReactionEventHandler),
            e.Guild.Id, e.Channel.Id, e.User.Id, async ctx =>
            {
                var reactionEvent = new ReactionEventEntity
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    UserDiscordId = e.User.Id,
                    GuildDiscordId = e.Guild.Id,
                    EmoteDiscordId = e.Emoji.Id,
                    EmoteName = e.Emoji.Name ?? string.Empty,
                    IsAnimated = e.Emoji.IsAnimated,
                    IsBurst = false,
                    EventType = ReactionEventType.Added,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                ctx.Db.ReactionEvents.Add(reactionEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageReactionRemovedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessageReactionRemoved", nameof(ReactionEventHandler),
            e.Guild.Id, e.Channel.Id, e.User.Id, async ctx =>
            {
                var reactionEvent = new ReactionEventEntity
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    UserDiscordId = e.User.Id,
                    GuildDiscordId = e.Guild.Id,
                    EmoteDiscordId = e.Emoji.Id,
                    EmoteName = e.Emoji.Name ?? string.Empty,
                    IsAnimated = e.Emoji.IsAnimated,
                    IsBurst = false,
                    EventType = ReactionEventType.Removed,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                ctx.Db.ReactionEvents.Add(reactionEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageReactionsClearedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessageReactionsCleared", nameof(ReactionEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var reactionEvent = new ReactionEventEntity
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    UserDiscordId = 0,
                    GuildDiscordId = e.Guild.Id,
                    EmoteDiscordId = null,
                    EmoteName = string.Empty,
                    IsAnimated = false,
                    IsBurst = false,
                    EventType = ReactionEventType.Cleared,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                ctx.Db.ReactionEvents.Add(reactionEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageReactionRemovedEmojiEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessageReactionRemovedEmoji", nameof(ReactionEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var reactionEvent = new ReactionEventEntity
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    UserDiscordId = 0,
                    GuildDiscordId = e.Guild.Id,
                    EmoteDiscordId = e.Emoji.Id,
                    EmoteName = e.Emoji.Name ?? string.Empty,
                    IsAnimated = e.Emoji.IsAnimated,
                    IsBurst = false,
                    EventType = ReactionEventType.EmojiCleared,
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                ctx.Db.ReactionEvents.Add(reactionEvent);
                await ctx.Db.SaveChangesAsync();
            });
    }
}
