using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            var guildUpsert = scope.ServiceProvider.GetRequiredService<GuildUpsertService>();
            var channelUpsert = scope.ServiceProvider.GetRequiredService<ChannelUpsertService>();
            var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

            rawJson = await rawEventService.SerializeAndLogAsync(
                e, "MessageCreated", e.Guild.Id, e.Channel.Id, e.Author.Id);

            // Persist the raw event row immediately. Otherwise any 23505 race inside one of the
            // upsert services below clears the change tracker on its catch path and the staged
            // raw_event_logs row is silently dropped along with the rolled-back insert.
            await db.SaveChangesAsync();

            await userService.UpsertUserAsync(e.Author);

            var attachmentsJson = e.Message.Attachments.Count > 0
                ? JsonSerializer.Serialize(e.Message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                : null;
            var embedsJson = e.Message.Embeds.Count > 0
                ? JsonSerializer.Serialize(e.Message.Embeds)
                : null;

            // Resolve FKs via upsert-if-missing. The 2026-05-03 incident left 69 messages with
            // channel_id IS NULL because the prior FirstOrDefault path silently wrote null when
            // the channel (a thread) wasn't yet in our DB. §P1.9 closes that hole; §P2.6 (#74)
            // makes the FKs NOT NULL so any regression in the upsert services becomes a loud
            // skip + FailedEvent instead of a silent NULL FK row.
            var guildId = await guildUpsert.UpsertGuildAsync(e.Guild);
            var channelId = await channelUpsert.UpsertChannelAsync(e.Channel, guildId);
            var authorId = await db.Users
                .Where(u => u.DiscordId == e.Author.Id)
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

            if (guildId == Guid.Empty || channelId == Guid.Empty || authorId == Guid.Empty)
            {
                logger.LogError(
                    "MessageCreated: could not resolve required FKs for MessageId={MessageId} (guildId={GuildId} channelId={ChannelId} authorId={AuthorId}); skipping insert",
                    e.Message.Id, guildId, channelId, authorId);
                using var fkFailScope = scopeFactory.CreateScope();
                var failedEventService = fkFailScope.ServiceProvider.GetRequiredService<FailedEventService>();
                await failedEventService.RecordFailureAsync(
                    "MessageCreated", nameof(MessageEventHandler),
                    new InvalidOperationException(
                        $"Required FK not resolved: guild={guildId} channel={channelId} author={authorId}"),
                    e.Guild?.Id, e.Channel.Id, e.Author.Id, rawJson);
                return;
            }

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
                    ChannelId = channelId,
                    GuildId = guildId,
                    AuthorId = authorId,
                    Content = e.Message.Content,
                    ReplyToDiscordId = e.Message.ReferencedMessage?.Id,
                    HasAttachments = e.Message.Attachments.Count > 0,
                    HasEmbeds = e.Message.Embeds.Count > 0,
                    AttachmentsJson = attachmentsJson,
                    EmbedsJson = embedsJson,
                    Flags = (int)(e.Message.Flags ?? 0),
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
                            .SetProperty(m => m.Flags, (int)(e.Message.Flags ?? 0))
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
            var flagsAfter = (int)(e.Message.Flags ?? 0);

            // Get the message entity for FK reference + before-state fallback when DSharpPlus
            // didn't deliver e.MessageBefore (uncached message edit).
            var message = await db.Messages.FirstOrDefaultAsync(m => m.DiscordId == e.Message.Id);
            var messageGuid = message?.Id;

            var contentBefore = e.MessageBefore is { } beforeForContent
                ? beforeForContent.Content
                : message?.Content;
            var attachmentsBeforeJson = e.MessageBefore is { } beforeForAttachments
                ? (beforeForAttachments.Attachments.Count > 0
                    ? JsonSerializer.Serialize(beforeForAttachments.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                    : null)
                : message?.AttachmentsJson;
            var embedsBeforeJson = e.MessageBefore is { } beforeForEmbeds
                ? (beforeForEmbeds.Embeds.Count > 0
                    ? JsonSerializer.Serialize(beforeForEmbeds.Embeds)
                    : null)
                : message?.EmbedsJson;
            int? flagsBefore = e.MessageBefore is { } before
                ? (int)(before.Flags ?? 0)
                : message?.Flags;

            // SerializeAndLogAsync staged a RawEventLog row but did NOT save it; flush it
            // before the strategy lambda's ChangeTracker.Clear() detaches it.
            await db.SaveChangesAsync();

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
                        .SetProperty(m => m.Flags, flagsAfter)
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

                var contentChanged = contentBefore != e.Message.Content;
                // jsonb columns get PG-normalized on storage; freshly-serialized strings
                // (from e.Message or e.MessageBefore) won't byte-match the DB-loaded value
                // even when the underlying data is identical. Structural compare.
                var attachmentsChanged = !JsonEquals(attachmentsBeforeJson, attachmentsJson);
                var embedsChanged = !JsonEquals(embedsBeforeJson, embedsJson);
                var flagsChanged = flagsBefore != flagsAfter;

                if (messageGuid is Guid mid &&
                    (contentChanged || attachmentsChanged || embedsChanged || flagsChanged))
                {
                    db.MessageEditHistory.Add(new MessageEditHistoryEntity
                    {
                        MessageId = mid,
                        MessageDiscordId = e.Message.Id,
                        ContentBefore = contentBefore,
                        ContentAfter = e.Message.Content,
                        AttachmentsBeforeJson = attachmentsBeforeJson,
                        AttachmentsAfterJson = attachmentsJson,
                        EmbedsBeforeJson = embedsBeforeJson,
                        EmbedsAfterJson = embedsJson,
                        FlagsBefore = flagsBefore,
                        FlagsAfter = flagsAfter,
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

    private static bool JsonEquals(string? a, string? b)
    {
        if (a == b) return true;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
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
