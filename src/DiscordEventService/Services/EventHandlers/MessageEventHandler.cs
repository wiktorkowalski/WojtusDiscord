using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using DSharpPlus;
using DSharpPlus.Entities;
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

        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
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
                    e, "MessageCreated", e.Guild.Id, e.Channel.Id, e.Author.Id, correlationId: correlationId);

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
                        e.Guild?.Id, e.Channel.Id, e.Author.Id, rawJson, correlationId: correlationId);
                    return;
                }

                MessageEventEntity NewMessageEvent() => new()
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    AuthorDiscordId = e.Author.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MessageEventType.Created,
                    Content = NormalizeContent(e.Message.Content),
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

                    var messageEntity = new MessageEntity
                    {
                        DiscordId = e.Message.Id,
                        ChannelId = channelId,
                        GuildId = guildId,
                        AuthorId = authorId,
                        Content = NormalizeContent(e.Message.Content),
                        ReplyToDiscordId = e.Message.ReferencedMessage?.Id,
                        HasAttachments = e.Message.Attachments.Count > 0,
                        HasEmbeds = e.Message.Embeds.Count > 0,
                        AttachmentsJson = attachmentsJson,
                        EmbedsJson = embedsJson,
                        Flags = (int)(e.Message.Flags ?? 0),
                        CreatedAtUtc = e.Message.Timestamp.UtcDateTime
                    };
                    db.Messages.Add(messageEntity);
                    db.MessageEvents.Add(NewMessageEvent());

                    Guid messageId;
                    try
                    {
                        await db.SaveChangesAsync();
                        messageId = messageEntity.Id;
                    }
                    catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        db.ChangeTracker.Clear();
                        var normalizedContent = NormalizeContent(e.Message.Content);
                        await db.Messages
                            .Where(m => m.DiscordId == e.Message.Id)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(m => m.Content, normalizedContent)
                                .SetProperty(m => m.HasAttachments, e.Message.Attachments.Count > 0)
                                .SetProperty(m => m.HasEmbeds, e.Message.Embeds.Count > 0)
                                .SetProperty(m => m.AttachmentsJson, attachmentsJson)
                                .SetProperty(m => m.EmbedsJson, embedsJson)
                                .SetProperty(m => m.Flags, (int)(e.Message.Flags ?? 0))
                                .SetProperty(m => m.ReplyToDiscordId, e.Message.ReferencedMessage?.Id));
                        db.MessageEvents.Add(NewMessageEvent());
                        await db.SaveChangesAsync();
                        messageId = await db.Messages
                            .Where(m => m.DiscordId == e.Message.Id)
                            .Select(m => m.Id)
                            .FirstAsync();
                    }

                    await ExtractAndSaveMentionsAsync(db, messageId, e.Message);
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
                    e.Guild?.Id, e.Channel.Id, e.Author.Id, rawJson, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageUpdatedEventArgs e)
    {
        if (e.Guild is null) return;

        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                var receivedAt = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                var rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "MessageUpdated", e.Guild.Id, e.Channel.Id, e.Author?.Id, correlationId: correlationId);

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

                var contentBefore = NormalizeContent(e.MessageBefore is { } beforeForContent
                    ? beforeForContent.Content
                    : message?.Content);
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

                    var normalizedContent = NormalizeContent(e.Message.Content);
                    await db.Messages
                        .Where(m => m.DiscordId == e.Message.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.Content, normalizedContent)
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
                        Content = normalizedContent,
                        ContentBefore = contentBefore,
                        HasAttachments = e.Message.Attachments.Count > 0,
                        HasEmbeds = e.Message.Embeds.Count > 0,
                        ReplyToMessageDiscordId = e.Message.ReferencedMessage?.Id,
                        AttachmentsJson = attachmentsJson,
                        EmbedsJson = embedsJson,
                        EventTimestampUtc = editedAt,
                        ReceivedAtUtc = receivedAt,
                        RawEventJson = rawJson
                    });

                    var contentChanged = contentBefore != normalizedContent;
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
                            ContentAfter = normalizedContent,
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

                    if (messageGuid is Guid mentionMid)
                    {
                        await ExtractAndSaveMentionsAsync(db, mentionMid, e.Message);
                    }

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
                    e.Guild?.Id, e.Channel.Id, e.Author?.Id, correlationId: correlationId);
            }
        }
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                var receivedAt = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                var rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "MessageDeleted", e.Guild.Id, e.Channel.Id, e.Message.Author?.Id, correlationId: correlationId);

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
                    Content = NormalizeContent(e.Message.Content),
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
                    e.Guild?.Id, e.Channel.Id, e.Message.Author?.Id, correlationId: correlationId);
            }
        }
    }

    private static async Task ExtractAndSaveMentionsAsync(DiscordDbContext db, Guid messageId, DiscordMessage message)
    {
        await db.MessageMentions
            .Where(m => m.MessageId == messageId)
            .ExecuteDeleteAsync();

        var mentions = new List<MessageMentionEntity>();

        if (message.MentionedUsers is { Count: > 0 })
        {
            foreach (var user in message.MentionedUsers)
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedUserDiscordId = user.Id,
                    MentionType = MessageMentionType.User
                });
            }
        }

        if (message.MentionedRoles is { Count: > 0 })
        {
            foreach (var role in message.MentionedRoles)
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedRoleDiscordId = role.Id,
                    MentionType = MessageMentionType.Role
                });
            }
        }

        if (message.MentionedChannels is { Count: > 0 })
        {
            foreach (var channel in message.MentionedChannels)
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedChannelDiscordId = channel.Id,
                    MentionType = MessageMentionType.Channel
                });
            }
        }

        if (message.MentionEveryone)
        {
            var content = message.Content ?? "";
            if (content.Contains("@everyone"))
                mentions.Add(new MessageMentionEntity { MessageId = messageId, MentionType = MessageMentionType.Everyone });
            if (content.Contains("@here"))
                mentions.Add(new MessageMentionEntity { MessageId = messageId, MentionType = MessageMentionType.Here });
        }

        if (mentions.Count > 0)
        {
            db.MessageMentions.AddRange(mentions);
            await db.SaveChangesAsync();
        }
    }

    private static string? NormalizeContent(string? content) =>
        string.IsNullOrEmpty(content) ? null : content;

    private static bool JsonEquals(string? a, string? b)
    {
        if (a == b) return true;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));
    }

    public async Task HandleEventAsync(DiscordClient sender, MessagesBulkDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        var correlationId = Guid.NewGuid();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                var receivedAt = DateTime.UtcNow;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
                var rawEventService = scope.ServiceProvider.GetRequiredService<RawEventLogService>();

                var rawJson = await rawEventService.SerializeAndLogAsync(
                    e, "MessagesBulkDeleted", e.Guild.Id, e.Channel.Id, null, correlationId: correlationId);

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
                    Content = NormalizeContent(msg.Content),
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
                    e.Guild?.Id, e.Channel.Id, null, correlationId: correlationId);
            }
        }
    }
}
