using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.EventHandlers;

internal sealed class MessageEventHandler(EventPipeline pipeline) :
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
                var attachmentsJson = e.Message.Attachments.Count > 0
                    ? JsonSerializer.Serialize(e.Message.Attachments.Select(a => new { a.Id, a.Url, a.FileName, a.FileSize }))
                    : null;
                var embedsJson = e.Message.Embeds.Count > 0
                    ? JsonSerializer.Serialize(e.Message.Embeds)
                    : null;

                // Resolve FKs via upsert-if-missing: a FirstOrDefault path would silently write a NULL
                // FK when the channel (e.g. an uncached thread) isn't in our DB yet. Resolver does an
                // all-or-fail validation so a missing FK becomes a loud skip, not a NULL-FK row.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, e.Channel, e.Author, $"MessageId={e.Message.Id}");
                if (!fks.Success) return; // resolver already logged + recorded the failure

                var guildId = fks.GuildId;
                var channelId = fks.ChannelId;
                var authorId = fks.UserId;

                MessageEventEntity NewMessageEvent() => new MessageEventEntity
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
                    RawEventJson = ctx.RawJson,
                };

                // Wrap message + event + mentions in one transaction so they commit atomically.
                // ExecutionStrategy is required because EnableRetryOnFailure is configured on the
                // DbContext. MessageCreated is insert-or-ignore: a duplicate carries identical
                // create-time content (edits arrive as MessageUpdated), so on a 23505 race the
                // existing row is kept untouched and we just log the Created event.
                var strategy = ctx.Db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    ctx.Db.ChangeTracker.Clear();
                    await using var tx = await ctx.Db.Database.BeginTransactionAsync();

                    // Insert the message first (before staging the event), so the primitive's
                    // change-tracker clear on a conflict can't drop a pending event Add.
                    var (messageEntity, _) = await ctx.Db.Messages.GetOrInsertAsync(
                        m => m.DiscordId == e.Message.Id,
                        () => new MessageEntity
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
                            CreatedAtUtc = e.Message.Timestamp.UtcDateTime,
                        });

                    var messageId = messageEntity!.Id;
                    ctx.Db.MessageEvents.Add(NewMessageEvent());
                    await ctx.Db.SaveChangesAsync();

                    await ExtractAndSaveMentionsAsync(ctx.Db, messageId, e.Message);
                    await tx.CommitAsync();
                });

                EnqueueMemeIndexing(ctx, e);
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
                var flagsBefore = e.MessageBefore is { } before
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
                        RawEventJson = ctx.RawJson,
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
                            RecordedAtUtc = ctx.ReceivedAtUtc,
                        });
                    }

                    await ctx.Db.SaveChangesAsync();

                    if (messageGuid is Guid mentionMid)
                        await ExtractAndSaveMentionsAsync(ctx.Db, mentionMid, e.Message);

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
                    RawEventJson = ctx.RawJson,
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
                    RawEventJson = ctx.RawJson,
                });

                ctx.Db.MessageEvents.AddRange(events);
                await ctx.Db.SaveChangesAsync();
            });
    }

    // Failures are swallowed (warning only) — indexing must never break message persistence,
    // and the weekly sweep heals anything missed.
    private static void EnqueueMemeIndexing(EventContext ctx, MessageCreatedEventArgs e)
    {
        try
        {
            if (e.Message.Attachments.Count == 0)
                return;

            var memeOptions = ctx.Services.GetRequiredService<IOptions<MemeIndexOptions>>().Value;
            if (!memeOptions.ChannelIds.Contains(e.Channel.Id))
                return;
            if (!e.Message.Attachments.Any(a => a.FileName is not null && ImageMagic.IsIndexableFileName(a.FileName)))
                return;

            ctx.Services.GetRequiredService<IBackgroundJobClient>()
                .Enqueue<MemeIndexingJob>(j => j.IndexMessageAsync(e.Guild.Id, e.Message.Id, CancellationToken.None));
            ctx.Logger.LogDebug("Live meme indexing enqueued for message {MessageId}", e.Message.Id);
        }
        catch (Exception ex)
        {
            ctx.Logger.LogWarning(ex,
                "Failed to enqueue live meme indexing for message {MessageId} — the weekly sweep will heal it",
                e.Message.Id);
        }
    }

    private static async Task ExtractAndSaveMentionsAsync(DiscordDbContext db, Guid messageId, DiscordMessage message)
    {
        await db.MessageMentions
            .Where(m => m.MessageId == messageId)
            .ExecuteDeleteAsync();

        List<MessageMentionEntity> mentions = [];

        if (message.MentionedUsers is { Count: > 0 })
        {
            foreach (var user in message.MentionedUsers)
            {
                mentions.Add(new MessageMentionEntity
                {
                    MessageId = messageId,
                    MentionedUserDiscordId = user.Id,
                    MentionType = MessageMentionType.User,
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
                    MentionType = MessageMentionType.Role,
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
                    MentionType = MessageMentionType.Channel,
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
