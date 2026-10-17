using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// #460 / #468 against a real Postgres: what query_database may read is decided by row-level security on
// the query role, fed from Conversation:QueryDatabase through QuerySandboxSettingsWriter. Each message-
// bearing table gets the same pair: hidden with the defaults, visible with the switch off. The app
// login (the superuser EF uses for ingestion, and meme_search) still sees everything.
public sealed class DatabaseQueryVisibilityTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 9100UL;
    private const ulong VisibleChannelDiscordId = 9200UL;
    private const ulong HiddenChannelDiscordId = 9201UL;
    private const ulong ThreadInHiddenChannelDiscordId = 9202UL;

    private const ulong LiveMessageId = 9300UL;
    private const ulong DeletedMessageId = 9301UL;
    private const ulong HiddenChannelMessageId = 9302UL;
    private const ulong ThreadMessageId = 9303UL;
    // Only a delete event, no messages row: the bot saw the delete of a message it never stored.
    private const ulong DeleteEventOnlyMessageId = 9304UL;

    private DiscordDbContext _db = null!;
    private readonly Dictionary<ulong, Guid> _messageUuids = [];

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.MessageEvents.ExecuteDeleteAsync();
        await _db.PollEvents.ExecuteDeleteAsync();
        await _db.ReactionEvents.ExecuteDeleteAsync();
        await _db.PinEvents.ExecuteDeleteAsync();
        await _db.MemeAnnotations.ExecuteDeleteAsync();
        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.AutoModEvents.ExecuteDeleteAsync();
        await _db.MessageMentions.ExecuteDeleteAsync();
        await _db.MessageEditHistory.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        // Back to the migration's state: no settings row, no hidden channel.
        await _db.Database.ExecuteSqlAsync($"DELETE FROM query_sandbox_settings");
        await _db.Database.ExecuteSqlAsync($"DELETE FROM query_sandbox_hidden_channels");

        var guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        var author = new UserEntity { DiscordId = 9400UL, Username = "u" };
        _db.AddRange(guild, author);
        await _db.SaveChangesAsync();

        var visible = Channel(guild, VisibleChannelDiscordId, ChannelType.Text);
        var hidden = Channel(guild, HiddenChannelDiscordId, ChannelType.Text);
        var thread = Channel(guild, ThreadInHiddenChannelDiscordId, ChannelType.PublicThread, HiddenChannelDiscordId);
        _db.AddRange(visible, hidden, thread);
        await _db.SaveChangesAsync();

        AddMessage(guild, author, visible, LiveMessageId, deleted: false);
        AddMessage(guild, author, visible, DeletedMessageId, deleted: true);
        AddMessage(guild, author, hidden, HiddenChannelMessageId, deleted: false);
        AddMessage(guild, author, thread, ThreadMessageId, deleted: false);
        _db.MessageEvents.Add(Event(DeleteEventOnlyMessageId, VisibleChannelDiscordId, MessageEventType.Deleted));
        _db.ReactionEvents.Add(Reaction(DeleteEventOnlyMessageId, VisibleChannelDiscordId));
        _db.PinEvents.AddRange(Pin(VisibleChannelDiscordId), Pin(HiddenChannelDiscordId), Pin(ThreadInHiddenChannelDiscordId));
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    public static TheoryData<string> MessageTables =>
    [
        "messages", "message_edit_history", "message_mentions", "message_events", "auto_mod_events",
        "poll_events", "reaction_events", "meme_index", "meme_annotations",
    ];

    [Theory]
    [MemberData(nameof(MessageTables))]
    public async Task DeletedMessage_IsHiddenByDefault_AndVisibleWithTheSwitchOff(string table)
    {
        await ApplyAsync(new QueryDatabaseOptions());
        Assert.Equal(0, await CountAsync(table, DeletedMessageId));
        Assert.Equal(1, await CountAsync(table, LiveMessageId));

        await ApplyAsync(new QueryDatabaseOptions { ExcludeDeletedMessages = false });
        Assert.Equal(1, await CountAsync(table, DeletedMessageId));
    }

    [Theory]
    [MemberData(nameof(MessageTables))]
    public async Task HiddenChannel_HidesItsMessagesAndItsThreads_AndUnlistingShowsThem(string table)
    {
        await ApplyAsync(new QueryDatabaseOptions { HiddenChannelIds = [HiddenChannelDiscordId] });
        Assert.Equal(0, await CountAsync(table, HiddenChannelMessageId));
        Assert.Equal(0, await CountAsync(table, ThreadMessageId));
        Assert.Equal(1, await CountAsync(table, LiveMessageId));

        await ApplyAsync(new QueryDatabaseOptions());
        Assert.Equal(1, await CountAsync(table, HiddenChannelMessageId));
        Assert.Equal(1, await CountAsync(table, ThreadMessageId));
    }

    [Fact]
    public async Task DeleteEventWithoutMessageRow_IsHiddenByDefault()
    {
        await ApplyAsync(new QueryDatabaseOptions());
        Assert.Equal(0, await CountAsync("message_events", DeleteEventOnlyMessageId));
        Assert.Equal(0, await CountAsync("reaction_events", DeleteEventOnlyMessageId));

        await ApplyAsync(new QueryDatabaseOptions { ExcludeDeletedMessages = false });
        Assert.Equal(1, await CountAsync("message_events", DeleteEventOnlyMessageId));
        Assert.Equal(1, await CountAsync("reaction_events", DeleteEventOnlyMessageId));
    }

    [Fact]
    public async Task HiddenChannel_HidesItsPinsAndItsThreadsPins()
    {
        // pin_events carries a channel only, so deletion does not apply to it.
        await ApplyAsync(new QueryDatabaseOptions { HiddenChannelIds = [HiddenChannelDiscordId] });
        Assert.Equal(1, await CountPinsAsync(VisibleChannelDiscordId));
        Assert.Equal(0, await CountPinsAsync(HiddenChannelDiscordId));
        Assert.Equal(0, await CountPinsAsync(ThreadInHiddenChannelDiscordId));

        await ApplyAsync(new QueryDatabaseOptions());
        Assert.Equal(1, await CountPinsAsync(HiddenChannelDiscordId));
        Assert.Equal(1, await CountPinsAsync(ThreadInHiddenChannelDiscordId));
    }

    [Fact]
    public async Task NoSettingsWrittenYet_StillHidesDeleted()
    {
        // No settings row at all (the writer never ran): the visibility functions fail closed.
        Assert.Equal(0, await CountAsync("messages", DeletedMessageId));
    }

    [Fact]
    public async Task TheModelCannotFlipTheSwitch()
    {
        await ApplyAsync(new QueryDatabaseOptions { HiddenChannelIds = [HiddenChannelDiscordId] });
        var service = NewService();

        // No GUC is read, so setting one (even session-wide) changes nothing.
        await service.RunAsync("SELECT set_config('wojtus.include_deleted', 'on', false)", CancellationToken.None);
        Assert.Equal(0, await CountAsync("messages", DeletedMessageId));

        foreach (var table in new[] { "query_sandbox_settings", "query_sandbox_hidden_channels", "raw_event_logs", "failed_events" })
        {
            var (text, failed) = await service.RunAsync($"SELECT * FROM {table}", CancellationToken.None);
            Assert.True(failed, $"{table}: {text}");
            Assert.Contains("permission denied", text);
        }

        // A write is refused by the read-only transaction before any privilege check.
        var (updateText, updateFailed) = await service.RunAsync(
            "WITH x AS (UPDATE query_sandbox_settings SET exclude_deleted_messages = false RETURNING 1) SELECT * FROM x",
            CancellationToken.None);
        Assert.True(updateFailed, updateText);
        Assert.Equal(0, await CountAsync("messages", DeletedMessageId));
        Assert.Equal(0, await CountAsync("messages", HiddenChannelMessageId));
    }

    [Fact]
    public async Task TheAppLogin_StillSeesEveryRow()
    {
        await ApplyAsync(new QueryDatabaseOptions { HiddenChannelIds = [HiddenChannelDiscordId] });

        await using var context = NewContext();
        Assert.Equal(4, await context.Messages.CountAsync());
        Assert.Equal(4, await context.MessageEditHistory.CountAsync());
        Assert.Equal(4, await context.MessageMentions.CountAsync());
        Assert.Equal(5, await context.MessageEvents.CountAsync());
        Assert.Equal(4, await context.AutoModEvents.CountAsync());
        Assert.Equal(4, await context.PollEvents.CountAsync());
        Assert.Equal(5, await context.ReactionEvents.CountAsync());
        Assert.Equal(3, await context.PinEvents.CountAsync());
        Assert.Equal(4, await context.MemeIndex.CountAsync());
        Assert.Equal(4, await context.MemeAnnotations.CountAsync());
    }

    [Fact]
    public async Task Writer_ReplacesTheHiddenList()
    {
        await ApplyAsync(new QueryDatabaseOptions { HiddenChannelIds = [HiddenChannelDiscordId, 1UL, 1UL] });
        await ApplyAsync(new QueryDatabaseOptions { HiddenChannelIds = [VisibleChannelDiscordId] });

        var listed = await _db.Database
            .SqlQuery<long>($"""SELECT channel_discord_id AS "Value" FROM query_sandbox_hidden_channels""")
            .ToListAsync();
        Assert.Equal([(long)VisibleChannelDiscordId], listed);
        Assert.Equal(1, await CountAsync("messages", HiddenChannelMessageId));
        Assert.Equal(0, await CountAsync("messages", LiveMessageId));
    }

    [Fact]
    public async Task SchemaHint_DoesNotNameTheUnreadableTables()
    {
        await using var context = NewContext();
        var hint = DatabaseSchemaHint.Build(SchemaCatalog.Build(context.Model)).Text;

        Assert.DoesNotContain("raw_event_logs(", hint);
        Assert.DoesNotContain("failed_events(", hint);
        Assert.DoesNotContain("query_sandbox_", hint);
        Assert.Contains("messages(", hint);
    }

    // Counts the rows of one message in a table, as query_database sees them.
    private async Task<int> CountAsync(string table, ulong messageDiscordId)
    {
        var filter = table switch
        {
            "messages" => $"discord_id = {messageDiscordId}",
            "message_edit_history" or "message_events" or "auto_mod_events" or "poll_events" or "reaction_events"
                or "meme_index" => $"message_discord_id = {messageDiscordId}",
            // Each test message has one image, whose attachment id is the message id.
            "meme_annotations" => $"attachment_discord_id = {messageDiscordId}",
            "message_mentions" => _messageUuids.TryGetValue(messageDiscordId, out var uuid)
                ? $"message_id = '{uuid}'"
                : "false",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return await QueryCountAsync($"SELECT count(*)::int AS n FROM {table} WHERE {filter}");
    }

    private Task<int> CountPinsAsync(ulong channelDiscordId) =>
        QueryCountAsync($"SELECT count(*)::int AS n FROM pin_events WHERE channel_discord_id = {channelDiscordId}");

    private async Task<int> QueryCountAsync(string sql)
    {
        var (text, failed) = await NewService().RunAsync(sql, CancellationToken.None);
        Assert.False(failed, text);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("rows")[0].GetProperty("n").GetInt32();
    }

    private async Task ApplyAsync(QueryDatabaseOptions options)
    {
        await using var context = NewContext();
        await QuerySandboxSettingsWriter.ApplyAsync(context, options, NullLogger.Instance, CancellationToken.None);
    }

    private static ChannelEntity Channel(GuildEntity guild, ulong discordId, ChannelType type, ulong? parent = null) =>
        new() { DiscordId = discordId, GuildId = guild.Id, Name = $"c{discordId}", Type = type, ParentDiscordId = parent };

    private void AddMessage(GuildEntity guild, UserEntity author, ChannelEntity channel, ulong discordId, bool deleted)
    {
        var message = new MessageEntity
        {
            Id = Guid.NewGuid(),
            DiscordId = discordId,
            ChannelId = channel.Id,
            GuildId = guild.Id,
            AuthorId = author.Id,
            Content = $"text {discordId}",
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = deleted,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null,
        };
        _messageUuids[discordId] = message.Id;
        _db.Messages.Add(message);
        _db.MessageEditHistory.Add(new MessageEditHistoryEntity
        {
            MessageId = message.Id,
            MessageDiscordId = discordId,
            ContentBefore = "before",
            ContentAfter = message.Content,
            EditedAtUtc = DateTime.UtcNow,
        });
        _db.MessageMentions.Add(new MessageMentionEntity
        {
            MessageId = message.Id,
            MentionedUserDiscordId = author.DiscordId,
            MentionType = MessageMentionType.User,
        });
        _db.MessageEvents.Add(Event(discordId, channel.DiscordId, MessageEventType.Created));
        _db.AutoModEvents.Add(new AutoModEventEntity
        {
            GuildDiscordId = GuildDiscordId,
            RuleDiscordId = 9500UL,
            EventType = AutoModEventType.ActionExecuted,
            ChannelDiscordId = channel.DiscordId,
            MessageDiscordId = discordId,
            Content = message.Content,
            MatchedContent = "text",
            EventTimestampUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
        });
        _db.PollEvents.Add(new PollEventEntity
        {
            MessageDiscordId = discordId,
            ChannelDiscordId = channel.DiscordId,
            GuildDiscordId = GuildDiscordId,
            UserDiscordId = author.DiscordId,
            AnswerId = 1,
            EventType = PollEventType.VoteAdded,
            EventTimestampUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
        });
        _db.ReactionEvents.Add(Reaction(discordId, channel.DiscordId));
        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = message.Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = channel.DiscordId,
            MessageDiscordId = discordId,
            AttachmentDiscordId = discordId,
            FileName = "meme.png",
            FileSizeBytes = 1,
            Status = MemeIndexStatus.Indexed,
            Annotations =
            [
                new MemeAnnotationEntity
                {
                    AttachmentDiscordId = discordId,
                    ModelId = "m",
                    PromptVersion = "v",
                    IndexedAtUtc = DateTime.UtcNow,
                    OcrText = message.Content,
                },
            ],
        });
    }

    private static ReactionEventEntity Reaction(ulong messageDiscordId, ulong channelDiscordId) => new()
    {
        MessageDiscordId = messageDiscordId,
        ChannelDiscordId = channelDiscordId,
        GuildDiscordId = GuildDiscordId,
        UserDiscordId = 9400UL,
        EmoteName = "x",
        EventType = ReactionEventType.Added,
        EventTimestampUtc = DateTime.UtcNow,
        ReceivedAtUtc = DateTime.UtcNow,
    };

    private static PinEventEntity Pin(ulong channelDiscordId) => new()
    {
        ChannelDiscordId = channelDiscordId,
        GuildDiscordId = GuildDiscordId,
        EventTimestampUtc = DateTime.UtcNow,
        ReceivedAtUtc = DateTime.UtcNow,
    };

    private static MessageEventEntity Event(ulong messageDiscordId, ulong channelDiscordId, MessageEventType type) => new()
    {
        MessageDiscordId = messageDiscordId,
        ChannelDiscordId = channelDiscordId,
        GuildDiscordId = GuildDiscordId,
        EventType = type,
        Content = "text",
        EventTimestampUtc = DateTime.UtcNow,
        ReceivedAtUtc = DateTime.UtcNow,
    };

    private DatabaseQueryService NewService() =>
        new(NewContext(), Options.Create(new ConversationOptions()), NullLogger<DatabaseQueryService>.Instance);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
