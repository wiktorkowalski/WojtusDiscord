using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Infrastructure;
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

        // From the event alone, before any database work: a message the pipeline fails still counts.
        var length = e.Message.Content?.Length ?? 0;
        var attachmentCount = e.Message.Attachments.Count;
        BotMetrics.MessageCreated(
            MessageKindOf(length, attachmentCount, e.Message.Stickers?.Count ?? 0, e.Message.Embeds.Count),
            e.Author.IsBot,
            isReply: e.Message.ReferencedMessage is not null,
            length,
            attachmentCount);

        await pipeline.ExecuteAsync(e, "MessageCreated", nameof(MessageEventHandler),
            e.Guild.Id, e.Channel.Id, e.Author.Id, async ctx =>
            {
                var attachmentsJson = MessageJson.Attachments(e.Message.Attachments);
                var embedsJson = MessageJson.Embeds(e.Message.Embeds);

                // Resolve FKs via upsert-if-missing: a FirstOrDefault path would silently write a NULL
                // FK when the channel (e.g. an uncached thread) isn't in our DB yet. Resolver does an
                // all-or-fail validation so a missing FK becomes a loud skip, not a NULL-FK row.
                var fks = await ctx.Services.GetRequiredService<FkResolver>()
                    .ResolveAsync(ctx, e.Guild, e.Channel, e.Author, $"MessageId={e.Message.Id}");
                if (!fks.Success) return; // resolver already logged + recorded the failure

                var guildId = fks.GuildId;
                var channelId = fks.ChannelId;
                var authorId = fks.UserId;

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
                        () => BuildMessage(e, guildId, channelId, authorId, attachmentsJson, embedsJson));

                    var messageId = messageEntity!.Id;
                    ctx.Db.MessageEvents.Add(BuildMessageCreatedEvent(e, ctx, attachmentsJson, embedsJson));
                    await ctx.Db.SaveChangesAsync();

                    await SaveMentionsAsync(ctx.Db, messageId, CollectMentions(messageId, e.Message));
                    await tx.CommitAsync();
                });

                EnqueueMemeIndexing(ctx, e);
            });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageUpdatedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessageUpdated", nameof(MessageEventHandler),
            e.Guild.Id, e.Channel.Id, e.Author?.Id, ctx => ApplyUpdateAsync(
                ctx,
                new MessageUpdate(
                    e.Message.Id, e.Channel.Id, e.Guild.Id, e.Author?.Id, e.Message.ReferencedMessage?.Id,
                    StateOf(e.Message), e.Message.EditedTimestamp?.UtcDateTime),
                e.MessageBefore is { } cached ? StateOf(cached) : null,
                messageId => CollectMentions(messageId, e.Message)));
    }

    // The write path of MESSAGE_UPDATE, free of DSharpPlus types so a test can drive it.
    // cachedBefore is the DSharpPlus-cached message; without it the stored row is the before-state.
    // mentionsOf is a callback because a mention row needs the message Guid, known only after the lookup.
    internal static async Task ApplyUpdateAsync(
        EventContext ctx, MessageUpdate update, MessageState? cachedBefore,
        Func<Guid, List<MessageMentionEntity>> mentionsOf)
    {
        // A MESSAGE_UPDATE without flags is stored as 0, as before #413.
        var flagsAfter = update.After.Flags ?? 0;
        var after = update.After with { Flags = flagsAfter };
        var editedAt = update.EditedTimestampUtc ?? ctx.ReceivedAtUtc;

        var message = await ctx.Db.Messages.FirstOrDefaultAsync(m => m.DiscordId == update.MessageDiscordId);
        var stored = new MessageState(message?.Content, message?.AttachmentsJson, message?.EmbedsJson, message?.Flags);
        // The DSharpPlus cache can hold a message without its flags; 0 there would be a false flag change.
        var before = cachedBefore is null ? stored : cachedBefore with { Flags = cachedBefore.Flags ?? stored.Flags };

        // A Discord-side refresh (#413) still stores the new signed URLs and the full embed shape,
        // but it is no edit: no edit-history row.
        var isRealChange = MessageJson.IsRealChange(before, after);
        if (!isRealChange)
            ctx.Logger.LogDebug(
                "MessageUpdated for {MessageId} is a Discord-side refresh, not an edit", update.MessageDiscordId);

        // Wrap the ExecuteUpdate (auto-commits without a tx) and the event-log inserts
        // in one transaction so a failure between them rolls back atomically.
        var strategy = ctx.Db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            ctx.Db.ChangeTracker.Clear();
            await using var tx = await ctx.Db.Database.BeginTransactionAsync();

            await ctx.Db.Messages
                .Where(m => m.DiscordId == update.MessageDiscordId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Content, after.Content)
                    .SetProperty(m => m.HasAttachments, after.AttachmentsJson != null)
                    .SetProperty(m => m.HasEmbeds, after.EmbedsJson != null)
                    .SetProperty(m => m.AttachmentsJson, after.AttachmentsJson)
                    .SetProperty(m => m.EmbedsJson, after.EmbedsJson)
                    .SetProperty(m => m.Flags, flagsAfter)
                    // Discord's edited_timestamp only, as backfill writes it: an unfurl or a refresh is no edit.
                    .SetProperty(m => m.EditedAtUtc, m => update.EditedTimestampUtc ?? m.EditedAtUtc));

            ctx.Db.MessageEvents.Add(BuildMessageUpdatedEvent(update, after, ctx, before.Content, editedAt));

            if (message is not null && isRealChange)
                ctx.Db.MessageEditHistory.Add(BuildEditHistory(update, after, ctx, message.Id, before, editedAt));

            await ctx.Db.SaveChangesAsync();

            if (message is not null)
                await SaveMentionsAsync(ctx.Db, message.Id, mentionsOf(message.Id));

            await tx.CommitAsync();
        });
    }

    public async Task HandleEventAsync(DiscordClient sender, MessageDeletedEventArgs e)
    {
        if (e.Guild is null) return;

        await pipeline.ExecuteAsync(e, "MessageDeleted", nameof(MessageEventHandler),
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

        await pipeline.ExecuteAsync(e, "MessagesBulkDeleted", nameof(MessageEventHandler),
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
            if (!memeOptions.AutomaticIndexing || !memeOptions.ChannelIds.Contains(e.Channel.Id))
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

    private static async Task SaveMentionsAsync(DiscordDbContext db, Guid messageId, List<MessageMentionEntity> mentions)
    {
        await db.MessageMentions
            .Where(m => m.MessageId == messageId)
            .ExecuteDeleteAsync();

        if (mentions.Count > 0)
        {
            db.MessageMentions.AddRange(mentions);
            await db.SaveChangesAsync();
        }
    }

    private static List<MessageMentionEntity> CollectMentions(Guid messageId, DiscordMessage message)
    {
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

        return mentions;
    }

    private static string? NormalizeContent(string? content) =>
        string.IsNullOrEmpty(content) ? null : content;

    private static MessageState StateOf(DiscordMessage message) => new(
        NormalizeContent(message.Content),
        MessageJson.Attachments(message.Attachments),
        MessageJson.Embeds(message.Embeds),
        (int?)message.Flags);

    // The kind label of wojtus_messages_total: what the message mainly carries, the first that
    // holds. An embed of a link arrives later, in MessageUpdated, so "embed" here is a message
    // that came with one (a bot or a webhook) and a link alone is "text".
    internal static string MessageKindOf(int length, int attachmentCount, int stickerCount, int embedCount) =>
        attachmentCount > 0 ? "attachment"
        : stickerCount > 0 ? "sticker"
        : embedCount > 0 ? "embed"
        : length > 0 ? "text"
        : "empty";

    private static MessageEntity BuildMessage(
        MessageCreatedEventArgs e, Guid guildId, Guid channelId, Guid authorId,
        string? attachmentsJson, string? embedsJson) => new MessageEntity
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
        };

    private static MessageEventEntity BuildMessageCreatedEvent(
        MessageCreatedEventArgs e, EventContext ctx, string? attachmentsJson, string? embedsJson) => new MessageEventEntity
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

    private static MessageEventEntity BuildMessageUpdatedEvent(
        MessageUpdate update, MessageState after, EventContext ctx, string? contentBefore, DateTime editedAt) => new MessageEventEntity
        {
            MessageDiscordId = update.MessageDiscordId,
            ChannelDiscordId = update.ChannelDiscordId,
            AuthorDiscordId = update.AuthorDiscordId,
            GuildDiscordId = update.GuildDiscordId,
            EventType = MessageEventType.Updated,
            Content = after.Content,
            ContentBefore = contentBefore,
            HasAttachments = after.AttachmentsJson != null,
            HasEmbeds = after.EmbedsJson != null,
            ReplyToMessageDiscordId = update.ReplyToMessageDiscordId,
            AttachmentsJson = after.AttachmentsJson,
            EmbedsJson = after.EmbedsJson,
            EventTimestampUtc = editedAt,
            ReceivedAtUtc = ctx.ReceivedAtUtc,
            RawEventJson = ctx.RawJson,
        };

    private static MessageEditHistoryEntity BuildEditHistory(
        MessageUpdate update, MessageState after, EventContext ctx, Guid messageId, MessageState before, DateTime editedAt) => new MessageEditHistoryEntity
        {
            MessageId = messageId,
            MessageDiscordId = update.MessageDiscordId,
            ContentBefore = before.Content,
            ContentAfter = after.Content,
            AttachmentsBeforeJson = before.AttachmentsJson,
            AttachmentsAfterJson = after.AttachmentsJson,
            EmbedsBeforeJson = before.EmbedsJson,
            EmbedsAfterJson = after.EmbedsJson,
            FlagsBefore = before.Flags,
            FlagsAfter = after.Flags,
            EditedAtUtc = editedAt,
            RecordedAtUtc = ctx.ReceivedAtUtc,
        };
}

// A MESSAGE_UPDATE as ApplyUpdateAsync needs it. EditedTimestampUtc is Discord's edited_timestamp:
// null for an embed unfurl, a flag change or a Discord-side refresh.
internal sealed record MessageUpdate(
    ulong MessageDiscordId,
    ulong ChannelDiscordId,
    ulong GuildDiscordId,
    ulong? AuthorDiscordId,
    ulong? ReplyToMessageDiscordId,
    MessageState After,
    DateTime? EditedTimestampUtc);
