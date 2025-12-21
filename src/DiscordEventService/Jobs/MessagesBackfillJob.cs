using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

public class MessagesBackfillJob(
    IServiceScopeFactory scopeFactory,
    DiscordClient discordClient,
    ILogger<MessagesBackfillJob> logger) : BackfillJobBase, IBackfillJob
{
    protected override BackfillType BackfillType => BackfillType.Messages;

    private const int BatchSize = 100;
    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromMilliseconds(500);

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
                logger.LogWarning("Guild {GuildId} not found in database, cannot backfill messages", guildId);
                await MarkFailedAsync(db, checkpoint, new InvalidOperationException($"Guild {guildId} not found in database"));
                return;
            }

            // Filter to text-based channels
            var textChannels = channels
                .Where(c => c.Type is DSharpPlus.Entities.DiscordChannelType.Text
                         or DSharpPlus.Entities.DiscordChannelType.News
                         or DSharpPlus.Entities.DiscordChannelType.PublicThread
                         or DSharpPlus.Entities.DiscordChannelType.PrivateThread
                         or DSharpPlus.Entities.DiscordChannelType.NewsThread)
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

                logger.LogInformation("Backfilling messages for channel {ChannelName} ({ChannelId}) in guild {GuildId}",
                    channel.Name, channel.Id, guildId);

                try
                {
                    await BackfillChannelMessagesAsync(db, userService, guildEntity, channel, checkpoint, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to backfill messages for channel {ChannelId}, continuing with next channel", channel.Id);
                    await RecordErrorAsync(db, checkpoint, ex);
                }

                // Reset for next channel
                checkpoint.LastProcessedId = null;
                await db.SaveChangesAsync(cancellationToken);
            }

            await MarkCompletedAsync(db, checkpoint);
            logger.LogInformation("Messages backfill completed for guild {GuildId}: {Count} messages", guildId, checkpoint.ProcessedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Messages backfill failed for guild {GuildId}", guildId);
            await MarkFailedAsync(db, checkpoint, ex);
            throw;
        }
    }

    private async Task BackfillChannelMessagesAsync(
        DiscordDbContext db,
        UserService userService,
        GuildEntity guildEntity,
        DiscordChannel channel,
        BackfillCheckpointEntity checkpoint,
        CancellationToken cancellationToken)
    {
        var channelEntity = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channel.Id, cancellationToken);

        if (channelEntity is null)
        {
            logger.LogWarning("Channel {ChannelId} not found in database, messages will have null channel FK", channel.Id);
        }

        ulong? beforeId = checkpoint.LastProcessedId;
        bool hasMore = true;

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                        var attachmentsJson = message.Attachments.Count > 0
                            ? JsonSerializer.Serialize(message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                            : null;
                        var embedsJson = message.Embeds.Count > 0
                            ? JsonSerializer.Serialize(message.Embeds.Select(e => new { e.Title, e.Description, e.Url }))
                            : null;

                        db.Messages.Add(new MessageEntity
                        {
                            DiscordId = message.Id,
                            ChannelId = channelEntity?.Id,
                            GuildId = guildEntity.Id,
                            AuthorId = authorEntity?.Id,
                            Content = message.Content,
                            ReplyToDiscordId = message.ReferencedMessage?.Id,
                            HasAttachments = message.Attachments.Count > 0,
                            HasEmbeds = message.Embeds.Count > 0,
                            AttachmentsJson = attachmentsJson,
                            EmbedsJson = embedsJson,
                            CreatedAtUtc = message.Timestamp.UtcDateTime,
                            EditedAtUtc = message.EditedTimestamp?.UtcDateTime
                        });

                        checkpoint.ProcessedCount++;
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

            // Respect rate limits
            await Task.Delay(DelayBetweenBatches, cancellationToken);
        }
    }
}
