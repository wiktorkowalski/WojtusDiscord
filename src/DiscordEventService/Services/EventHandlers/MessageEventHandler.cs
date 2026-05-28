using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordEventService.Services.EventHandlers;

public sealed class MessageEventHandler(EventPipeline pipeline) :
    IEventHandler<MessageCreatedEventArgs>,
    IEventHandler<MessageUpdatedEventArgs>,
    IEventHandler<MessageDeletedEventArgs>,
    IEventHandler<MessagesBulkDeletedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.Execute(e, "MessageCreated", nameof(MessageEventHandler),
            e.Guild.Id, e.Channel.Id, e.Author.Id, async ctx =>
            {
                var userService = ctx.Services.GetRequiredService<UserService>();
                var guildUpsert = ctx.Services.GetRequiredService<GuildUpsertService>();
                var channelUpsert = ctx.Services.GetRequiredService<ChannelUpsertService>();

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
                var authorId = await ctx.Db.Users
                    .Where(u => u.DiscordId == e.Author.Id)
                    .Select(u => u.Id)
                    .FirstOrDefaultAsync();

                if (guildId == Guid.Empty || channelId == Guid.Empty || authorId == Guid.Empty)
                {
                    ctx.Logger.LogError(
                        "MessageCreated: could not resolve required FKs for MessageId={MessageId} (guildId={GuildId} channelId={ChannelId} authorId={AuthorId}); skipping insert",
                        e.Message.Id, guildId, channelId, authorId);
                    await ctx.RecordFailureAsync(new InvalidOperationException(
                        $"Required FK not resolved: guild={guildId} channel={channelId} author={authorId}"));
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
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                };

                // Wrap multi-row writes in a transaction so the 23505 retry path
                // (ExecuteUpdate + insert) is atomic. ExecutionStrategy is required
                // because EnableRetryOnFailure is configured on the DbContext.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

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
                    ctx.Db.Messages.Add(messageEntity);
                    ctx.Db.MessageEvents.Add(NewMessageEvent());

                    Guid messageId;
                    try
                    {
                        await ctx.Db.SaveChangesAsync();
                        messageId = messageEntity.Id;
                    }
                    catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                    {
                        ctx.Db.ChangeTracker.Clear();
                        var normalizedContent = NormalizeContent(e.Message.Content);
                        await ctx.Db.Messages
                            .Where(m => m.DiscordId == e.Message.Id)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(m => m.Content, normalizedContent)
                                .SetProperty(m => m.HasAttachments, e.Message.Attachments.Count > 0)
                                .SetProperty(m => m.HasEmbeds, e.Message.Embeds.Count > 0)
                                .SetProperty(m => m.AttachmentsJson, attachmentsJson)
                                .SetProperty(m => m.EmbedsJson, embedsJson)
                                .SetProperty(m => m.Flags, (int)(e.Message.Flags ?? 0))
                                .SetProperty(m => m.ReplyToDiscordId, e.Message.ReferencedMessage?.Id));
                        ctx.Db.MessageEvents.Add(NewMessageEvent());
                        await ctx.Db.SaveChangesAsync();
                        messageId = await ctx.Db.Messages
                            .Where(m => m.DiscordId == e.Message.Id)
                            .Select(m => m.Id)
                            .FirstAsync();
                    }

                    await ExtractAndSaveMentionsAsync(ctx.Db, messageId, e.Message);
                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageUpdatedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.Execute(e, "MessageUpdated", nameof(MessageEventHandler),
            e.Guild.Id, e.Channel.Id, e.Author?.Id, async ctx =>
            {
                var attachmentsJson = e.Message.Attachments.Count > 0
                    ? JsonSerializer.Serialize(e.Message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                    : null;
                var embedsJson = e.Message.Embeds.Count > 0
                    ? JsonSerializer.Serialize(e.Message.Embeds)
                    : null;
                var editedAt = e.Message.EditedTimestamp?.UtcDateTime ?? ctx.ReceivedAtUtc;
                var flagsAfter = (int)(e.Message.Flags ?? 0);

                // Get the message entity for FK reference + before-state fallback when DSharpPlus
                // didn't deliver e.MessageBefore (uncached message edit).
                var message = await ctx.Db.Messages.FirstOrDefaultAsync(m => m.DiscordId == e.Message.Id);
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

                // Wrap the ExecuteUpdate (auto-commits without a tx) and the event-log inserts
                // in one transaction so a failure between them rolls back atomically.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    var normalizedContent = NormalizeContent(e.Message.Content);
                    await ctx.Db.Messages
                        .Where(m => m.DiscordId == e.Message.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.Content, normalizedContent)
                            .SetProperty(m => m.HasAttachments, e.Message.Attachments.Count > 0)
                            .SetProperty(m => m.HasEmbeds, e.Message.Embeds.Count > 0)
                            .SetProperty(m => m.AttachmentsJson, attachmentsJson)
                            .SetProperty(m => m.EmbedsJson, embedsJson)
                            .SetProperty(m => m.Flags, flagsAfter)
                            .SetProperty(m => m.EditedAtUtc, editedAt));

                    ctx.Db.MessageEvents.Add(new MessageEventEntity
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
                        ReceivedAtUtc = ctx.ReceivedAtUtc,
                        RawEventJson = ctx.RawJson
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
                        ctx.Db.MessageEditHistory.Add(new MessageEditHistoryEntity
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
                            RecordedAtUtc = ctx.ReceivedAtUtc
                        });
                    }

                    await ctx.Db.SaveChangesAsync();

                    if (messageGuid is Guid mentionMid)
                    {
                        await ExtractAndSaveMentionsAsync(ctx.Db, mentionMid, e.Message);
                    }

                    await tx.CommitAsync();
                });
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.Execute(e, "MessageDeleted", nameof(MessageEventHandler),
            e.Guild.Id, e.Channel.Id, e.Message.Author?.Id, async ctx =>
            {
                await ctx.Db.Messages
                    .Where(m => m.DiscordId == e.Message.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.IsDeleted, true)
                        .SetProperty(m => m.DeletedAtUtc, ctx.ReceivedAtUtc));

                ctx.Db.MessageEvents.Add(new MessageEventEntity
                {
                    MessageDiscordId = e.Message.Id,
                    ChannelDiscordId = e.Channel.Id,
                    AuthorDiscordId = e.Message.Author?.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MessageEventType.Deleted,
                    Content = NormalizeContent(e.Message.Content),
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                await ctx.Db.SaveChangesAsync();
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessagesBulkDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.Execute(e, "MessagesBulkDeleted", nameof(MessageEventHandler),
            e.Guild.Id, e.Channel.Id, null, async ctx =>
            {
                var messageIds = e.Messages.Select(m => m.Id).ToList();
                await ctx.Db.Messages
                    .Where(m => messageIds.Contains(m.DiscordId))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.IsDeleted, true)
                        .SetProperty(m => m.DeletedAtUtc, ctx.ReceivedAtUtc));

                var events = e.Messages.Select(msg => new MessageEventEntity
                {
                    MessageDiscordId = msg.Id,
                    ChannelDiscordId = e.Channel.Id,
                    AuthorDiscordId = msg.Author?.Id,
                    GuildDiscordId = e.Guild.Id,
                    EventType = MessageEventType.BulkDeleted,
                    Content = NormalizeContent(msg.Content),
                    EventTimestampUtc = ctx.ReceivedAtUtc,
                    ReceivedAtUtc = ctx.ReceivedAtUtc,
                    RawEventJson = ctx.RawJson
                });

                ctx.Db.MessageEvents.AddRange(events);
                await ctx.Db.SaveChangesAsync();
            });
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
}
