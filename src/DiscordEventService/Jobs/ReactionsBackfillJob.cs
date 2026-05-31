using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public sealed class ReactionsBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor,
    ILogger<ReactionsBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Reactions;

    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromMilliseconds(200);

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => ExecuteAsync(guildId, null, cancellationToken);

    public Task ExecuteAsync(ulong guildId, DateTime? afterTimestampUtc, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var userService = ctx.Services.GetRequiredService<UserService>();

            var guild = await discordClient.GetGuildAsync(guildId);
            var channels = await guild.GetChannelsAsync();
            var guildEntity = await ctx.Db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
                return BackfillOutcome.ShortCircuit($"Guild {guildId} not found in database");

            // Get all text channels
            var textChannels = channels
                .Where(c => c.Type is DiscordChannelType.Text
                         or DiscordChannelType.News
                         or DiscordChannelType.PublicThread
                         or DiscordChannelType.PrivateThread
                         or DiscordChannelType.NewsThread)
                .OrderBy(c => c.Id)
                .ToList();

            // Resume from checkpoint channel if exists. The cursor reset (clear when the prior run
            // was not InProgress) is owned by BackfillJobExecutor, so CurrentChannelId here is either
            // null (fresh run) or the channel a genuinely-interrupted run stopped on.
            if (ctx.Checkpoint.CurrentChannelId.HasValue)
            {
                var checkpointChannelIndex = textChannels.FindIndex(c => c.Id == ctx.Checkpoint.CurrentChannelId.Value);
                if (checkpointChannelIndex >= 0)
                {
                    textChannels = textChannels.Skip(checkpointChannelIndex).ToList();
                }
                else
                {
                    logger.LogWarning("Checkpoint channel {ChannelId} not found in guild {GuildId}, restarting from beginning",
                        ctx.Checkpoint.CurrentChannelId.Value, guildId);
                    ctx.Checkpoint.CurrentChannelId = null;
                    ctx.Checkpoint.LastProcessedId = null;
                }
            }

            foreach (var channel in textChannels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ctx.Checkpoint.CurrentChannelId = channel.Id;
                await ctx.Db.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Backfilling reactions for channel {ChannelName} ({ChannelId}) in guild {GuildId}",
                    channel.Name, channel.Id, guildId);

                try
                {
                    await BackfillChannelReactionsAsync(ctx.Db, userService, guildEntity, channel, ctx.Checkpoint, afterTimestampUtc, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to backfill reactions for channel {ChannelId}, continuing with next channel", channel.Id);
                    await RecordErrorAsync(ctx.Db, ctx.Checkpoint, ex);
                }

                // Reset for next channel
                ctx.Checkpoint.LastProcessedId = null;
                await ctx.Db.SaveChangesAsync(cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);

    private async Task BackfillChannelReactionsAsync(
        DiscordDbContext db,
        UserService userService,
        GuildEntity guildEntity,
        DiscordChannel channel,
        BackfillCheckpointEntity checkpoint,
        DateTime? afterTimestampUtc,
        CancellationToken cancellationToken)
    {
        const int messageBatchSize = 100;
        ulong? beforeId = checkpoint.LastProcessedId;
        bool hasMore = true;

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<DiscordMessage> messages;
            try
            {
                var asyncMessages = beforeId.HasValue
                    ? channel.GetMessagesBeforeAsync(beforeId.Value, messageBatchSize)
                    : channel.GetMessagesAsync(messageBatchSize);

                messages = [];
                await foreach (var msg in asyncMessages)
                {
                    messages.Add(msg);
                }
            }
            catch (DSharpPlus.Exceptions.UnauthorizedException ex)
            {
                logger.LogWarning("No permission to read messages in channel {ChannelId}", channel.Id);
                await RecordErrorAsync(db, checkpoint, ex);
                break;
            }
            catch (DSharpPlus.Exceptions.NotFoundException ex)
            {
                logger.LogWarning("Channel {ChannelId} not found (may have been deleted)", channel.Id);
                await RecordErrorAsync(db, checkpoint, ex);
                break;
            }

            if (messages.Count == 0)
            {
                hasMore = false;
                break;
            }

            // Filter to messages with reactions
            var messagesWithReactions = messages
                .Where(m => m.Reactions != null && m.Reactions.Count > 0)
                .ToList();

            foreach (var message in messagesWithReactions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var reaction in message.Reactions)
                {
                    try
                    {
                        await BackfillMessageReactionAsync(db, userService, guildEntity, channel, message, reaction, checkpoint, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Failed to backfill reaction {Emoji} for message {MessageId}", reaction.Emoji.Name, message.Id);
                        await RecordErrorAsync(db, checkpoint, ex);
                    }
                }
            }

            // Save all reactions from this batch
            beforeId = messages.Last().Id;
            checkpoint.LastProcessedId = beforeId;
            await db.SaveChangesAsync(cancellationToken);

            // Stop scrolling once the oldest message in the batch is older than
            // the requested window (we've already collected reactions for the
            // current batch — overshoot by up to one batch is intentional).
            if (afterTimestampUtc.HasValue &&
                messages.Min(m => m.Timestamp).UtcDateTime < afterTimestampUtc.Value)
            {
                hasMore = false;
                break;
            }

            await Task.Delay(DelayBetweenBatches, cancellationToken);
        }
    }

    private async Task BackfillMessageReactionAsync(
        DiscordDbContext db,
        UserService userService,
        GuildEntity guildEntity,
        DiscordChannel channel,
        DiscordMessage message,
        DiscordReaction reaction,
        BackfillCheckpointEntity checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            // DSharpPlus v5: GetReactionsAsync returns all users for this emoji
            await foreach (var user in message.GetReactionsAsync(reaction.Emoji))
            {
                cancellationToken.ThrowIfCancellationRequested();

                await userService.UpsertUserAsync(user);

                // Check if this reaction event already exists
                var exists = await db.ReactionEvents.AnyAsync(r =>
                    r.MessageDiscordId == message.Id &&
                    r.UserDiscordId == user.Id &&
                    r.EmoteName == reaction.Emoji.Name &&
                    r.EventType == ReactionEventType.Backfilled,
                    cancellationToken);

                if (!exists)
                {
                    db.ReactionEvents.Add(new ReactionEventEntity
                    {
                        MessageDiscordId = message.Id,
                        ChannelDiscordId = channel.Id,
                        UserDiscordId = user.Id,
                        GuildDiscordId = guildEntity.DiscordId,
                        EmoteDiscordId = reaction.Emoji.Id,
                        EmoteName = reaction.Emoji.Name ?? string.Empty,
                        IsAnimated = reaction.Emoji.IsAnimated,
                        IsBurst = false,
                        EventType = ReactionEventType.Backfilled,
                        EventTimestampUtc = message.Timestamp.UtcDateTime,
                        ReceivedAtUtc = DateTime.UtcNow
                    });

                    checkpoint.ProcessedCount++;
                }
            }
            // Note: SaveChanges called per-batch in BackfillChannelReactionsAsync, not per-reaction
        }
        catch (DSharpPlus.Exceptions.NotFoundException)
        {
            logger.LogDebug("Message {MessageId} or reaction {Emoji} no longer exists, skipping",
                message.Id, reaction.Emoji.Name);
        }
    }
}
