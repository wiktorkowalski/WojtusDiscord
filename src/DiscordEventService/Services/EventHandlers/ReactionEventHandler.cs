using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

public class ReactionEventHandler(IServiceScopeFactory scopeFactory, ILogger<ReactionEventHandler> logger) :
    IEventHandler<MessageReactionAddedEventArgs>,
    IEventHandler<MessageReactionRemovedEventArgs>,
    IEventHandler<MessageReactionsClearedEventArgs>,
    IEventHandler<MessageReactionRemovedEmojiEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessageReactionAddedEventArgs e)
    {
        if (e.Guild is null) return;

        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageReactionAdded", e.Guild.Id, e.Channel.Id, e.User.Id);

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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.ReactionEvents.Add(reactionEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling reaction added for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageReactionAdded", nameof(ReactionEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, e.User.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageReactionRemovedEventArgs e)
    {
        if (e.Guild is null) return;

        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageReactionRemoved", e.Guild.Id, e.Channel.Id, e.User.Id);

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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.ReactionEvents.Add(reactionEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling reaction removed for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageReactionRemoved", nameof(ReactionEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, e.User.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageReactionsClearedEventArgs e)
    {
        if (e.Guild is null) return;

        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageReactionsCleared", e.Guild.Id, e.Channel.Id, null);

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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.ReactionEvents.Add(reactionEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling reactions cleared for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageReactionsCleared", nameof(ReactionEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, null, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageReactionRemovedEmojiEventArgs e)
    {
        if (e.Guild is null) return;

        string? rawJson = null;
        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageReactionRemovedEmoji", e.Guild.Id, e.Channel.Id, null);

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
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            };

            db.ReactionEvents.Add(reactionEvent);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling emoji cleared for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageReactionRemovedEmoji", nameof(ReactionEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, null, rawJson);
        }
    }
}
