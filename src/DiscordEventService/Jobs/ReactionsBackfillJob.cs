using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

internal sealed class ReactionsBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor,
    ILogger<ReactionsBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    private const int MessageBatchSize = 100;

    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromMilliseconds(200);

    protected override BackfillType BackfillType => BackfillType.Reactions;

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

            var threads = await ListGuildThreadsAsync(guild, channels, logger, cancellationToken);

            var textChannels = channels.Concat<DiscordChannel>(threads)
                .Where(c => c.Type is DiscordChannelType.Text
                         or DiscordChannelType.News
                         or DiscordChannelType.PublicThread
                         or DiscordChannelType.PrivateThread
                         or DiscordChannelType.NewsThread)
                .DistinctBy(c => c.Id)
                .OrderBy(c => c.Id)
                .ToList();

            // The per-channel resume-cursor loop (skip-to-resume + per-channel cursor management)
            // lives in BackfillJobBase; only this job's inner per-channel paging body is passed in.
            await IterateWithCheckpointAsync(ctx.Db, ctx.Checkpoint, textChannels, c => c.Id,
                async channel =>
                {
                    logger.LogInformation("Backfilling reactions for channel {ChannelName} ({ChannelId}) in guild {GuildId}",
                        channel.Name, channel.Id, guildId);
                    await BackfillChannelReactionsAsync(ctx.Db, userService, guildEntity, channel, ctx.Checkpoint, afterTimestampUtc, cancellationToken);
                },
                logger, cancellationToken);

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
        var beforeId = checkpoint.LastProcessedId;
        var hasMore = true;

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (messages, fetchFailure) = await FetchMessageBatchAsync(db, channel, checkpoint, beforeId, MessageBatchSize, logger, cancellationToken);
            if (fetchFailure is not null)
                break;

            if (messages!.Count == 0)
            {
                hasMore = false;
                break;
            }

            var messagesWithReactions = messages
                .Where(m => m.Reactions is { Count: > 0 })
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
                        await RecordErrorAsync(db, checkpoint, ex, cancellationToken);
                    }
                }
            }

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
