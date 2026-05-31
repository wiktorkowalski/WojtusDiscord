using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public sealed class MessagesBackfillJob(
    DiscordClient discordClient,
    BackfillJobExecutor executor,
    ILogger<MessagesBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Messages;

    private const int BatchSize = 100;
    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromMilliseconds(500);

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

            // Filter to text-based channels
            var textChannels = channels
                .Where(c => c.Type is DSharpPlus.Entities.DiscordChannelType.Text
                         or DSharpPlus.Entities.DiscordChannelType.News
                         or DSharpPlus.Entities.DiscordChannelType.PublicThread
                         or DSharpPlus.Entities.DiscordChannelType.PrivateThread
                         or DSharpPlus.Entities.DiscordChannelType.NewsThread)
                .OrderBy(c => c.Id)
                .ToList();

            // Resume from checkpoint channel if exists. The cursor reset (clear when the prior run
            // was not InProgress) is owned by BackfillJobExecutor, so CurrentChannelId here is either
            // null (fresh run) or the channel a genuinely-interrupted run stopped on. Carrying a
            // stale cursor over a completed run would Skip all-but-the-last channel and mask gaps.
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

                logger.LogInformation("Backfilling messages for channel {ChannelName} ({ChannelId}) in guild {GuildId}",
                    channel.Name, channel.Id, guildId);

                try
                {
                    await BackfillChannelMessagesAsync(ctx.Db, userService, guildEntity, channel, ctx.Checkpoint, afterTimestampUtc, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to backfill messages for channel {ChannelId}, continuing with next channel", channel.Id);
                    await RecordErrorAsync(ctx.Db, ctx.Checkpoint, ex);
                }

                // Reset for next channel
                ctx.Checkpoint.LastProcessedId = null;
                await ctx.Db.SaveChangesAsync(cancellationToken);
            }

            return BackfillOutcome.Completed;
        }, cancellationToken);

    private async Task BackfillChannelMessagesAsync(
        DiscordDbContext db,
        UserService userService,
        GuildEntity guildEntity,
        DiscordChannel channel,
        BackfillCheckpointEntity checkpoint,
        DateTime? afterTimestampUtc,
        CancellationToken cancellationToken)
    {
        var channelEntity = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channel.Id, cancellationToken);

        if (channelEntity is null)
        {
            logger.LogWarning("Channel {ChannelId} not found in database, messages will have null channel FK", channel.Id);
        }

        ulong? beforeId = checkpoint.LastProcessedId;
        bool hasMore = true;
        int batchNum = 0;
        int totalInserted = 0;
        string exitReason = "unknown";

        // #124 diagnostic: trace exactly why per-channel loops terminated at ~one batch
        // in auto-startup runs but went deep when manually triggered.
        logger.LogInformation(
            "[bf-diag] Channel {ChannelName} ({ChannelId}) starting; initial beforeId={BeforeId}",
            channel.Name, channel.Id, beforeId);

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchNum++;

            List<DiscordMessage> messages;
            try
            {
                var asyncMessages = beforeId.HasValue
                    ? channel.GetMessagesBeforeAsync(beforeId.Value, BatchSize)
                    : channel.GetMessagesAsync(BatchSize);

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
                exitReason = "UnauthorizedException";
                break;
            }
            catch (DSharpPlus.Exceptions.NotFoundException ex)
            {
                logger.LogWarning("Channel {ChannelId} not found (may have been deleted)", channel.Id);
                await RecordErrorAsync(db, checkpoint, ex);
                exitReason = "NotFoundException";
                break;
            }

            if (messages.Count == 0)
            {
                logger.LogInformation(
                    "[bf-diag] Channel {ChannelName} batch {BatchNum}: API returned 0 messages with beforeId={BeforeId} — terminating loop",
                    channel.Name, batchNum, beforeId);
                hasMore = false;
                exitReason = "API returned 0";
                break;
            }

            logger.LogInformation(
                "[bf-diag] Channel {ChannelName} batch {BatchNum}: got {Count} messages, newest={NewestId} ({NewestTs:O}), oldest={OldestId} ({OldestTs:O})",
                channel.Name, batchNum, messages.Count,
                messages.First().Id, messages.First().Timestamp.UtcDateTime,
                messages.Last().Id, messages.Last().Timestamp.UtcDateTime);

            foreach (var message in messages)
            {
                try
                {
                    if (message.Author is null)
                    {
                        logger.LogDebug("Message {MessageId} has null author, skipping", message.Id);
                        continue;
                    }

                    await userService.UpsertUserAsync(message.Author);

                    var authorEntity = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == message.Author.Id, cancellationToken);

                    // Check if message exists
                    var exists = await db.Messages.AnyAsync(m => m.DiscordId == message.Id, cancellationToken);
                    if (!exists)
                    {
                        // §P2.6: MessageEntity.ChannelId/GuildId/AuthorId are NOT NULL. Skip
                        // if any required FK row hasn't been backfilled yet — next run of
                        // the corresponding backfill job + the next MessagesBackfillJob will
                        // pick it up.
                        if (channelEntity is null || authorEntity is null)
                        {
                            logger.LogWarning(
                                "Skipping backfill insert for message {MessageId}: channelEntity={ChannelPresent} authorEntity={AuthorPresent}",
                                message.Id, channelEntity is not null, authorEntity is not null);
                            continue;
                        }
                        var attachmentsJson = message.Attachments.Count > 0
                            ? JsonSerializer.Serialize(message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                            : null;
                        var embedsJson = message.Embeds.Count > 0
                            ? JsonSerializer.Serialize(message.Embeds)
                            : null;

                        db.Messages.Add(new MessageEntity
                        {
                            DiscordId = message.Id,
                            ChannelId = channelEntity.Id,
                            GuildId = guildEntity.Id,
                            AuthorId = authorEntity.Id,
                            Content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
                            ReplyToDiscordId = message.ReferencedMessage?.Id,
                            HasAttachments = message.Attachments.Count > 0,
                            HasEmbeds = message.Embeds.Count > 0,
                            AttachmentsJson = attachmentsJson,
                            EmbedsJson = embedsJson,
                            Flags = (int)(message.Flags ?? 0),
                            CreatedAtUtc = message.Timestamp.UtcDateTime,
                            EditedAtUtc = message.EditedTimestamp?.UtcDateTime
                        });

                        // Symmetric provenance marker: every backfilled message gets a
                        // message_events row with EventType=Backfilled. Mirrors
                        // ReactionsBackfillJob's ReactionEventType.Backfilled pattern.
                        // RawEventJson is null because backfill came via REST, not gateway.
                        db.MessageEvents.Add(new MessageEventEntity
                        {
                            MessageDiscordId = message.Id,
                            ChannelDiscordId = channel.Id,
                            AuthorDiscordId = message.Author.Id,
                            GuildDiscordId = guildEntity.DiscordId,
                            EventType = MessageEventType.Backfilled,
                            Content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
                            HasAttachments = message.Attachments.Count > 0,
                            HasEmbeds = message.Embeds.Count > 0,
                            ReplyToMessageDiscordId = message.ReferencedMessage?.Id,
                            AttachmentsJson = attachmentsJson,
                            EmbedsJson = embedsJson,
                            EventTimestampUtc = message.Timestamp.UtcDateTime,
                            ReceivedAtUtc = DateTime.UtcNow,
                            RawEventJson = null
                        });

                        checkpoint.ProcessedCount++;
                        totalInserted++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process message {MessageId}", message.Id);
                    await RecordErrorAsync(db, checkpoint, ex);
                }
            }

            beforeId = messages.Last().Id;
            checkpoint.LastProcessedId = beforeId;
            await db.SaveChangesAsync(cancellationToken);

            // Stop scrolling once the oldest message in the batch is older than
            // the requested window. We still keep the messages we just inserted
            // (overshoot by up to one batch is safer than undershoot).
            if (afterTimestampUtc.HasValue &&
                messages.Min(m => m.Timestamp).UtcDateTime < afterTimestampUtc.Value)
            {
                hasMore = false;
                exitReason = $"afterTimestampUtc reached ({afterTimestampUtc:O})";
                break;
            }

            // Respect rate limits
            await Task.Delay(DelayBetweenBatches, cancellationToken);
        }

        logger.LogInformation(
            "[bf-diag] Channel {ChannelName} ({ChannelId}) done: batches={BatchCount}, inserted={Inserted}, exit_reason={ExitReason}, final_beforeId={BeforeId}",
            channel.Name, channel.Id, batchNum, totalInserted, exitReason, beforeId);
    }
}
