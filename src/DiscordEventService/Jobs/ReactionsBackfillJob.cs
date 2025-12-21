using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class ReactionsBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<ReactionsBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Reactions;

    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromMilliseconds(200);

    public async Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();

        var checkpoint = await GetOrCreateCheckpointAsync(db, guildId);
        checkpoint.Status = BackfillStatus.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var guild = await discordClient.GetGuildAsync(guildId);
            var channels = await guild.GetChannelsAsync();
            var guildEntity = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == guildId, cancellationToken);

            if (guildEntity is null)
            {
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill reactions", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            // Get all text channels
            var textChannels = channels
                .Where(c => c.Type is DiscordChannelType.Text or DiscordChannelType.News)
                .OrderBy(c => c.Id)
                .ToList();

            // Resume from checkpoint channel if exists
            if (checkpoint.CurrentChannelId.HasValue)
            {
                var checkpointChannelIndex = textChannels.FindIndex(c => c.Id == checkpoint.CurrentChannelId.Value);
                if (checkpointChannelIndex >= 0)
                {
                    textChannels = textChannels.Skip(checkpointChannelIndex).ToList();
                }
                else
                {
                    logger.LogWarning("Checkpoint channel {ChannelId} not found in guild {GuildId}, restarting from beginning",
                        checkpoint.CurrentChannelId.Value, guildId);
                    checkpoint.CurrentChannelId = null;
                    checkpoint.LastProcessedId = null;
                }
            }

            foreach (var channel in textChannels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                checkpoint.CurrentChannelId = channel.Id;
                await db.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Backfilling reactions for channel {ChannelName} ({ChannelId}) in guild {GuildId}",
                    channel.Name, channel.Id, guildId);

                try
                {
                    await BackfillChannelReactionsAsync(db, userService, guildEntity, channel, checkpoint, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to backfill reactions for channel {ChannelId}, continuing with next channel", channel.Id);
                    await RecordErrorAsync(db, checkpoint, ex);
                }

                // Reset for next channel
                checkpoint.LastProcessedId = null;
                await db.SaveChangesAsync(cancellationToken);
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("Reactions backfill completed for guild {GuildId}: {Count} reactions", guildId, checkpoint.ProcessedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reactions backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }

    private async Task BackfillChannelReactionsAsync(
        DiscordDbContext db,
        UserService userService,
        GuildEntity guildEntity,
        DiscordChannel channel,
        BackfillCheckpointEntity checkpoint,
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
