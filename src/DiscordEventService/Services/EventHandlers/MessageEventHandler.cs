using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DiscordEventService.Services.EventHandlers;

public class MessageEventHandler(IServiceScopeFactory scopeFactory, ILogger<MessageEventHandler> logger) :
    IEventHandler<MessageCreatedEventArgs>,
    IEventHandler<MessageUpdatedEventArgs>,
    IEventHandler<MessageDeletedEventArgs>,
    IEventHandler<MessagesBulkDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
    {
        if (e.Guild is null) return;

        string? rawJson = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageCreated", e.Guild.Id, e.Channel.Id, e.Author.Id);

            await userService.UpsertUserAsync(e.Author);

            var attachmentsJson = e.Message.Attachments.Count > 0
                ? JsonSerializer.Serialize(e.Message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                : null;
            var embedsJson = e.Message.Embeds.Count > 0
                ? JsonSerializer.Serialize(e.Message.Embeds)
                : null;

            // Look up related entities by DiscordId
            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.DiscordId == e.Guild.Id);
            var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == e.Channel.Id);
            var author = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == e.Author.Id);

            MessageEventEntity NewMessageEvent() => new()
            {
                MessageDiscordId = e.Message.Id,
                ChannelDiscordId = e.Channel.Id,
                AuthorDiscordId = e.Author.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MessageEventType.Created,
                Content = e.Message.Content,
                HasAttachments = e.Message.Attachments.Count > 0,
                HasEmbeds = e.Message.Embeds.Count > 0,
                ReplyToMessageDiscordId = e.Message.ReferencedMessage?.Id,
                AttachmentsJson = attachmentsJson,
                EmbedsJson = embedsJson,
                EventTimestampUtc = e.Message.Timestamp.UtcDateTime,
                ReceivedAtUtc = DateTime.UtcNow,
                RawEventJson = rawJson
            };

            // Wrap multi-row writes in a transaction so the 23505 retry path
            // (ExecuteUpdate + insert) is atomic. ExecutionStrategy is required
            // because EnableRetryOnFailure is configured on the DbContext.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();

                db.Messages.Add(new MessageEntity
                {
                    DiscordId = e.Message.Id,
                    ChannelId = channel?.Id,
                    GuildId = guild?.Id,
                    AuthorId = author?.Id,
                    Content = e.Message.Content,
                    ReplyToDiscordId = e.Message.ReferencedMessage?.Id,
                    HasAttachments = e.Message.Attachments.Count > 0,
                    HasEmbeds = e.Message.Embeds.Count > 0,
                    AttachmentsJson = attachmentsJson,
                    EmbedsJson = embedsJson,
                    CreatedAtUtc = e.Message.Timestamp.UtcDateTime
                });
                db.MessageEvents.Add(NewMessageEvent());

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    // Gateway redelivery: message already stored. Refresh mutable fields and record the event row.
                    db.ChangeTracker.Clear();
                    await db.Messages
                        .Where(m => m.DiscordId == e.Message.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.Content, e.Message.Content)
                            .SetProperty(m => m.HasAttachments, e.Message.Attachments.Count > 0)
                            .SetProperty(m => m.HasEmbeds, e.Message.Embeds.Count > 0)
                            .SetProperty(m => m.AttachmentsJson, attachmentsJson)
                            .SetProperty(m => m.EmbedsJson, embedsJson)
                            .SetProperty(m => m.ReplyToDiscordId, e.Message.ReferencedMessage?.Id));
                    db.MessageEvents.Add(NewMessageEvent());
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message created for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageCreated", nameof(MessageEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, e.Author.Id, rawJson);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageUpdatedEventArgs e)
    {
        if (e.Guild is null) return;

        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageUpdated", e.Guild.Id, e.Channel.Id, e.Author?.Id);

            var attachmentsJson = e.Message.Attachments.Count > 0
                ? JsonSerializer.Serialize(e.Message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                : null;
            var embedsJson = e.Message.Embeds.Count > 0
                ? JsonSerializer.Serialize(e.Message.Embeds)
                : null;
            var editedAt = e.Message.EditedTimestamp?.UtcDateTime ?? receivedAt;

            // Get the message entity for FK reference (read-only snapshot, used inside the tx)
            var message = await db.Messages.FirstOrDefaultAsync(m => m.DiscordId == e.Message.Id);
            var messageGuid = message?.Id;

            // Wrap the ExecuteUpdate (auto-commits without a tx) and the event-log inserts
            // in one transaction so a failure between them rolls back atomically.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();

                await db.Messages
                    .Where(m => m.DiscordId == e.Message.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Content, e.Message.Content)
                        .SetProperty(m => m.HasAttachments, e.Message.Attachments.Count > 0)
                        .SetProperty(m => m.HasEmbeds, e.Message.Embeds.Count > 0)
                        .SetProperty(m => m.AttachmentsJson, attachmentsJson)
                        .SetProperty(m => m.EmbedsJson, embedsJson)
                        .SetProperty(m => m.EditedAtUtc, editedAt));

                db.MessageEvents.Add(new MessageEventEntity
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    AuthorDiscordId = e.Author?.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MessageEventType.Updated,
                    Content = e.Message.Content,
                    ContentBefore = e.MessageBefore?.Content,
                    HasAttachments = e.Message.Attachments.Count > 0,
                    HasEmbeds = e.Message.Embeds.Count > 0,
                    ReplyToMessageDiscordId = e.Message.ReferencedMessage?.Id,
                    AttachmentsJson = attachmentsJson,
                    EmbedsJson = embedsJson,
                    EventTimestampUtc = editedAt,
                    ReceivedAtUtc = receivedAt,
                    RawEventJson = rawJson
                });

                if (e.MessageBefore?.Content != e.Message.Content)
                {
                    db.MessageEditHistory.Add(new MessageEditHistoryEntity
                    {
                        MessageId = messageGuid,
                        MessageDiscordId = e.Message.Id,
                        ContentBefore = e.MessageBefore?.Content,
                        ContentAfter = e.Message.Content,
                        EditedAtUtc = editedAt,
                        RecordedAtUtc = receivedAt
                    });
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message updated for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageUpdated", nameof(MessageEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, e.Author?.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageDeleted", e.Guild.Id, e.Channel.Id, e.Message.Author?.Id);

            // Mark message as deleted
            await db.Messages
                .Where(m => m.DiscordId == e.Message.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsDeleted, true)
                    .SetProperty(m => m.DeletedAtUtc, receivedAt));

            db.MessageEvents.Add(new MessageEventEntity
            {
                MessageDiscordId = e.Message.Id,
                ChannelDiscordId = e.Channel.Id,
                AuthorDiscordId = e.Message.Author?.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MessageEventType.Deleted,
                Content = e.Message.Content,
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling message deleted for MessageId={MessageId}", e.Message.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessageDeleted", nameof(MessageEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, e.Message.Author?.Id);
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessagesBulkDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        try
        {
            var receivedAt = DateTime.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            var rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessagesBulkDeleted", e.Guild.Id, e.Channel.Id, null);

            // Mark all messages as deleted
            var messageIds = e.Messages.Select(m => m.Id).ToList();
            await db.Messages
                .Where(m => messageIds.Contains(m.DiscordId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.IsDeleted, true)
                    .SetProperty(m => m.DeletedAtUtc, receivedAt));

            var events = e.Messages.Select(msg => new MessageEventEntity
            {
                MessageDiscordId = msg.Id,
                ChannelDiscordId = e.Channel.Id,
                AuthorDiscordId = msg.Author?.Id,
                GuildDiscordId = e.Guild.Id,
                EventType = MessageEventType.BulkDeleted,
                Content = msg.Content,
                EventTimestampUtc = receivedAt,
                ReceivedAtUtc = receivedAt,
                RawEventJson = rawJson
            });

            db.MessageEvents.AddRange(events);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling bulk message delete in ChannelId={ChannelId}", e.Channel.Id);
            using var failureScope = scopeFactory.CreateScope();
            var failedEventService = failureScope.ServiceProvider.GetRequiredService<FailedEventService>();
            await failedEventService.RecordFailureAsync(
                "MessagesBulkDeleted", nameof(MessageEventHandler), ex,
                e.Guild?.Id, e.Channel.Id, null);
        }
    }
}
