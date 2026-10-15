using System.Text.Json.Nodes;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DiscordEventService.Services.EventHandlers;
using DiscordEventService.Services.Pipeline;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// #413: Discord sends MESSAGE_UPDATE without edited_timestamp when it re-signs an attachment URL or
// refreshes an embed on an old message. That is no edit. The stored shapes below are the prod ones:
// a 3-field embed from May 2026 and an attachment with a signed CDN URL.
public sealed class MessageUpdateEditHistoryTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong MessageDiscordId = 500UL;
    private const string VideoUrl = "https://www.youtube.com/watch?v=MZ8dhiNHbTk";
    private const string ThumbnailUrl = "https://i.ytimg.com/vi/MZ8dhiNHbTk/hqdefault.jpg";
    private static readonly DateTime EditedBefore = new(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private DiscordDbContext _db = null!;
    private Guid _messageId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.MessageEditHistory.ExecuteDeleteAsync();
        await _db.MessageEvents.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = 1UL, Name = "g" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();
        var channel = new ChannelEntity { DiscordId = 2UL, GuildId = guild.Id, Name = "general", Type = ChannelType.Text };
        var author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(channel);
        _db.Users.Add(author);
        await _db.SaveChangesAsync();

        var message = new MessageEntity
        {
            DiscordId = MessageDiscordId,
            GuildId = guild.Id,
            ChannelId = channel.Id,
            AuthorId = author.Id,
            Content = "look",
            HasAttachments = true,
            HasEmbeds = true,
            AttachmentsJson = Attachments("ex=1&is=2&hm=3", 111),
            EmbedsJson = $$"""[{"Url": "{{VideoUrl}}", "Title": "what timeline", "Description": null}]""",
            Flags = 0,
            CreatedAtUtc = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EditedAtUtc = EditedBefore,
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();
        _messageId = message.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RefreshOfSignedUrlAndEmbedShape_WritesNoHistoryAndKeepsEditTime()
    {
        await ApplyAsync(Update(attachmentsJson: Attachments("ex=9&is=8&hm=7", 111)));

        Assert.Equal(0, await HistoryCountAsync());
        var message = await StoredAsync();
        Assert.Equal(EditedBefore, message.EditedAtUtc);

        // The refresh still stores the new signed URL and the full embed shape, with real URLs.
        Assert.Contains("ex=9", message.AttachmentsJson);
        var embed = JsonNode.Parse(message.EmbedsJson!)![0]!;
        Assert.Equal(ThumbnailUrl, embed["Thumbnail"]!["Url"]!.GetValue<string>());

        // The event log keeps every update, refresh or not.
        Assert.Equal(1, await _db.MessageEvents.CountAsync(e => e.MessageDiscordId == MessageDiscordId));
    }

    [Fact]
    public async Task RefreshCarryingAnOldEditTimestamp_WritesNoHistoryAndStoresThatTimestamp()
    {
        // Discord's refresh of a message edited long ago carries that edit's timestamp.
        var oldEdit = new DateTime(2023, 5, 31, 21, 36, 28, DateTimeKind.Utc);

        await ApplyAsync(Update(attachmentsJson: Attachments("ex=9&is=8&hm=7", 111), editedTimestamp: oldEdit));

        Assert.Equal(0, await HistoryCountAsync());
        Assert.Equal(oldEdit, (await StoredAsync()).EditedAtUtc);
    }

    [Fact]
    public async Task RefreshAgainstCachedFullShape_WritesNoHistory()
    {
        var update = Update(attachmentsJson: Attachments("ex=9&is=8&hm=7", 111));
        var cachedBefore = update.After with { AttachmentsJson = Attachments("ex=1&is=2&hm=3", 111) };

        await ApplyAsync(update, cachedBefore);

        Assert.Equal(0, await HistoryCountAsync());
    }

    [Fact]
    public async Task CachedBeforeWithoutFlags_UsesStoredFlags()
    {
        // Seen live: the DSharpPlus cache held the message without flags, so 0 → 1024 looked like a change.
        await _db.Messages.ExecuteUpdateAsync(s => s.SetProperty(m => m.Flags, 1024));
        var update = Update(flags: 1024);

        await ApplyAsync(update, cachedBefore: update.After with { Flags = null });

        Assert.Equal(0, await HistoryCountAsync());
    }

    [Fact]
    public async Task ContentEdit_WritesHistoryAndSetsEditTime()
    {
        var editedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

        await ApplyAsync(Update(content: "look again", editedTimestamp: editedAt));

        var row = await _db.MessageEditHistory.SingleAsync();
        Assert.Equal(("look", "look again"), (row.ContentBefore, row.ContentAfter));
        Assert.Equal(editedAt, (await StoredAsync()).EditedAtUtc);
    }

    [Fact]
    public async Task FlagChange_WritesHistory()
    {
        // SUPPRESS_EMBEDS, which Discord sends without edited_timestamp (#73).
        await ApplyAsync(Update(flags: 4));

        Assert.Equal(1, await HistoryCountAsync());
    }

    [Fact]
    public async Task NewAttachment_WritesHistory()
    {
        await ApplyAsync(Update(attachmentsJson: Attachments("ex=1&is=2&hm=3", 111, 112)));

        Assert.Equal(1, await HistoryCountAsync());
    }

    [Fact]
    public async Task EmbedTitleChange_WritesHistory()
    {
        await ApplyAsync(Update(embedTitle: "another title"));

        Assert.Equal(1, await HistoryCountAsync());
    }

    [Fact]
    public async Task FirstEmbedUnfurl_WritesHistory()
    {
        // The first unfurl after send (#73): the stored row has no embed yet.
        await _db.Messages.ExecuteUpdateAsync(s => s.SetProperty(m => m.EmbedsJson, (string?)null));

        await ApplyAsync(Update());

        var row = await _db.MessageEditHistory.SingleAsync();
        Assert.Null(row.EmbedsBeforeJson);
        Assert.NotNull(row.EmbedsAfterJson);

        // An unfurl is no edit of the message: only Discord's edited_timestamp sets the stored edit time.
        Assert.Equal(EditedBefore, (await StoredAsync()).EditedAtUtc);
    }

    private async Task ApplyAsync(MessageUpdate update, MessageState? cachedBefore = null)
    {
        await using var db = NewContext();
        var ctx = new EventContext(
            db, null!, Guid.NewGuid(), null, DateTime.UtcNow, NullLogger.Instance, _ => Task.CompletedTask);
        await MessageEventHandler.ApplyUpdateAsync(ctx, update, cachedBefore, _ => []);
    }

    private static MessageUpdate Update(
        string content = "look",
        string? attachmentsJson = null,
        string embedTitle = "what timeline",
        int flags = 0,
        DateTime? editedTimestamp = null)
    {
        var embed = new DiscordEmbedBuilder()
            .WithUrl(VideoUrl)
            .WithTitle(embedTitle)
            .WithThumbnail(ThumbnailUrl, 1280, 720)
            .Build();
        var after = new MessageState(
            content, attachmentsJson ?? Attachments("ex=1&is=2&hm=3", 111), MessageJson.Embeds([embed]), flags);
        return new MessageUpdate(MessageDiscordId, 2UL, 1UL, 3UL, null, after, editedTimestamp);
    }

    // The 4-field shape MessageJson.Attachments writes, with a signed CDN URL.
    private static string Attachments(string signature, params ulong[] ids) =>
        "[" + string.Join(",", ids.Select(id =>
            $$"""{"Id": {{id}}, "Url": "https://cdn.discordapp.com/attachments/2/{{id}}/clip.mp4?{{signature}}&", "FileName": "clip.mp4", "FileSize": 1024}""")) + "]";

    private Task<int> HistoryCountAsync() => _db.MessageEditHistory.CountAsync(h => h.MessageId == _messageId);

    private Task<MessageEntity> StoredAsync() => _db.Messages.AsNoTracking().SingleAsync(m => m.Id == _messageId);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
