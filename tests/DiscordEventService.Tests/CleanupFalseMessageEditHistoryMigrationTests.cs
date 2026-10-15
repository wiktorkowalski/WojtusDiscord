using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

// The one-off cleanup of #413: history rows that Discord's refreshes left, and the edited_at_utc they
// stamped. Every other class starts from the migrated template, where this migration saw empty
// tables. All reads and writes are raw SQL: the EF model fits the newest schema only.
public sealed class CleanupFalseMessageEditHistoryMigrationTests : IAsyncLifetime
{
    // One message per case, in the shapes prod holds. Each history row is numbered (HistoryRow).
    // 101 attachment URL re-signed, no edited_timestamp in the raw event        refresh
    // 102 stored embed in the narrow shape, the live one in the full shape      refresh
    // 103 a re-signed URL (131) and a later content edit (132)                  refresh + real
    // 104 content change   105 flag change   106 first link unfurl              real
    // 107 attachment added   108 embed title change                             real
    // 109 the same embed again, no raw MessageUpdated at all                    refresh
    private const string SeedSql = """
        INSERT INTO guilds (id, discord_id, name, owner_id, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000001', 1, 'g', 0, now(), now());

        INSERT INTO channels (id, discord_id, guild_id, name, type, position, is_nsfw, is_deleted, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000002', 2, '00000000-0000-0000-0000-000000000001', 'general', 0, 0, false, false, now(), now());

        INSERT INTO users (id, discord_id, username, is_bot, is_system, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000003', 3, 'u', false, false, now(), now());

        INSERT INTO messages (id, discord_id, channel_id, guild_id, author_id, created_at_utc, edited_at_utc, flags,
            has_attachments, has_embeds, is_deleted, first_seen_utc, last_updated_utc)
        SELECT ('00000000-0000-0000-0000-' || lpad(v.discord_id::text, 12, '0'))::uuid, v.discord_id,
            '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001',
            '00000000-0000-0000-0000-000000000003', '2023-05-01 00:00:00+00', v.edited_at_utc::timestamptz, 0,
            false, false, false, now(), now()
        FROM (VALUES
            (101, '2026-09-01 10:00:00+00'), (102, '2026-09-02 10:00:00+00'), (103, '2026-09-04 10:00:00+00'),
            (104, '2026-09-05 10:00:00+00'), (105, '2026-09-06 10:00:00+00'), (106, '2026-09-07 10:00:00+00'),
            (107, '2026-09-08 10:00:00+00'), (108, '2026-09-09 10:00:00+00'), (109, '2026-09-10 10:00:00+00')
        ) AS v(discord_id, edited_at_utc);

        INSERT INTO message_edit_history (id, message_id, message_discord_id, edited_at_utc, recorded_at_utc,
            content_before, content_after, flags_before, flags_after,
            attachments_before_json, attachments_after_json, embeds_before_json, embeds_after_json)
        SELECT ('00000000-0000-0000-0001-' || lpad(v.n::text, 12, '0'))::uuid, m.id, m.discord_id,
            v.edited_at_utc::timestamptz, v.edited_at_utc::timestamptz,
            v.content_before, v.content_after, v.flags_before, v.flags_after,
            v.attachments_before::jsonb, v.attachments_after::jsonb, v.embeds_before::jsonb, v.embeds_after::jsonb
        FROM (VALUES
            (101, 101, '2026-09-01 10:00:00+00', 'zdjęcie', 'zdjęcie', 0, 0,
             '[{"Id": 501, "Url": "https://cdn.discordapp.com/attachments/2/501/a.png?ex=6650a1b2&is=664f5032&hm=aaa", "FileName": "a.png", "FileSize": 10}]',
             '[{"Id": 501, "Url": "https://cdn.discordapp.com/attachments/2/501/a.png?ex=6677dd12&is=66768b92&hm=bbb", "FileName": "a.png", "FileSize": 10}]',
             NULL, NULL),
            (102, 102, '2026-09-02 10:00:00+00', 'https://example.com/a', 'https://example.com/a', 0, 0, NULL, NULL,
             '[{"Url": "https://example.com/a", "Title": "T", "Description": "D"}]',
             '[{"Type": "article", "Url": "https://example.com/a", "Title": "T", "Description": "D", "Color": null, "Thumbnail": {"Url": "https://example.com/t.png"}, "Provider": {"Name": "Example"}}]'),
            (131, 103, '2026-09-03 10:00:00+00', 'stare', 'stare', 0, 0,
             '[{"Id": 502, "Url": "https://cdn.discordapp.com/attachments/2/502/b.png?ex=1&is=1&hm=aaa", "FileName": "b.png", "FileSize": 10}]',
             '[{"Id": 502, "Url": "https://cdn.discordapp.com/attachments/2/502/b.png?ex=2&is=2&hm=bbb", "FileName": "b.png", "FileSize": 10}]',
             NULL, NULL),
            (132, 103, '2026-09-04 10:00:00+00', 'stare', 'nowe', 0, 0,
             '[{"Id": 502, "Url": "https://cdn.discordapp.com/attachments/2/502/b.png?ex=2&is=2&hm=bbb", "FileName": "b.png", "FileSize": 10}]',
             '[{"Id": 502, "Url": "https://cdn.discordapp.com/attachments/2/502/b.png?ex=2&is=2&hm=bbb", "FileName": "b.png", "FileSize": 10}]',
             NULL, NULL),
            (104, 104, '2026-09-05 10:00:00+00', 'literówka', 'literówki', 0, 0, NULL, NULL, NULL, NULL),
            (105, 105, '2026-09-06 10:00:00+00', 'https://example.com/c', 'https://example.com/c', 0, 4, NULL, NULL,
             '[{"Url": "https://example.com/c", "Title": "C", "Description": null}]', NULL),
            (106, 106, '2026-09-07 10:00:00+00', 'https://example.com/d', 'https://example.com/d', 0, 0, NULL, NULL,
             NULL, '[{"Url": "https://example.com/d", "Title": "D", "Description": "opis"}]'),
            (107, 107, '2026-09-08 10:00:00+00', 'dwa', 'dwa', 0, 0,
             '[{"Id": 503, "Url": "https://cdn.discordapp.com/attachments/2/503/c.png?ex=1&is=1&hm=aaa", "FileName": "c.png", "FileSize": 10}]',
             '[{"Id": 503, "Url": "https://cdn.discordapp.com/attachments/2/503/c.png?ex=1&is=1&hm=aaa", "FileName": "c.png", "FileSize": 10}, {"Id": 504, "Url": "https://cdn.discordapp.com/attachments/2/504/d.png?ex=1&is=1&hm=aaa", "FileName": "d.png", "FileSize": 10}]',
             NULL, NULL),
            (108, 108, '2026-09-09 10:00:00+00', 'https://example.com/e', 'https://example.com/e', 0, 0, NULL, NULL,
             '[{"Url": "https://example.com/e", "Title": "Stary tytuł", "Description": "D"}]',
             '[{"Url": "https://example.com/e", "Title": "Nowy tytuł", "Description": "D"}]'),
            (109, 109, '2026-09-10 10:00:00+00', 'https://example.com/f', 'https://example.com/f', 0, 0, NULL, NULL,
             '[{"Url": "https://example.com/f", "Title": "F", "Description": "D"}]',
             '[{"Url": "https://example.com/f", "Title": "F", "Description": "D"}]')
        ) AS v(n, message, edited_at_utc, content_before, content_after, flags_before, flags_after,
               attachments_before, attachments_after, embeds_before, embeds_after)
        JOIN messages AS m ON m.discord_id = v.message;

        -- 102: the latest MessageUpdated counts, not an older one, another message or another event type.
        -- 103: has an edited_timestamp too, which must not replace a real edit.
        INSERT INTO raw_event_logs (event_type, event_json, guild_discord_id, json_size_bytes, received_at_utc)
        VALUES
            ('MessageUpdated', '{"message": {"id": 101, "content": "zdjęcie"}}', 1, 0, '2026-09-01 10:00:00+00'),
            ('MessageUpdated', '{"message": {"id": 102, "content": "https://example.com/a"}}', 1, 0, '2026-09-01 09:00:00+00'),
            ('MessageUpdated', '{"message": {"id": 102, "edited_timestamp": "2023-05-31T21:36:28.654+00:00"}}', 1, 0, '2026-09-02 10:00:00+00'),
            ('MessageCreated', '{"message": {"id": 102, "edited_timestamp": "2026-01-01T00:00:00+00:00"}}', 1, 0, '2026-09-03 10:00:00+00'),
            ('MessageUpdated', '{"message": {"id": 999, "edited_timestamp": "2026-01-01T00:00:00+00:00"}}', 1, 0, '2026-09-04 10:00:00+00'),
            ('MessageUpdated', '{"message": {"id": 103, "edited_timestamp": "2026-09-04T09:59:59+00:00"}}', 1, 0, '2026-09-04 10:00:00+00');
        """;

    // One replay for the class: a seeded database at the migration before this one. Every test
    // changes the data, so each one works on its own clone of it.
    private static readonly Lazy<Task<SeededTemplate>> Template = new(CreateSeededTemplateAsync);

    private string _connectionString = null!;
    private string _migration = null!;

    public async Task InitializeAsync()
    {
        (var database, _migration) = await Template.Value;
        _connectionString = await PostgresFixture.CreateDatabaseAsync(database);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Up_AttachmentUrlResigned_RowIsDeleted()
    {
        await MigrateToAsync(_migration);

        Assert.DoesNotContain(HistoryRow(101), await ReadHistoryIdsAsync());
    }

    [Fact]
    public async Task Up_EmbedStoredInTheNarrowShapeAndLiveInTheFullShape_RowIsDeleted()
    {
        await MigrateToAsync(_migration);

        Assert.DoesNotContain(HistoryRow(102), await ReadHistoryIdsAsync());
    }

    [Fact]
    public async Task Up_SameEmbedAgain_RowIsDeleted()
    {
        await MigrateToAsync(_migration);

        Assert.DoesNotContain(HistoryRow(109), await ReadHistoryIdsAsync());
    }

    // Content, flags, a first unfurl (#73), an added attachment and a new embed title.
    [Theory]
    [InlineData(104)]
    [InlineData(105)]
    [InlineData(106)]
    [InlineData(107)]
    [InlineData(108)]
    public async Task Up_RealChange_RowIsKept(int row)
    {
        await MigrateToAsync(_migration);

        Assert.Contains(HistoryRow(row), await ReadHistoryIdsAsync());
    }

    [Fact]
    public async Task Up_RefreshAndRealEditOfOneMessage_OnlyTheRefreshRowIsDeleted()
    {
        await MigrateToAsync(_migration);

        var ids = await ReadHistoryIdsAsync();
        Assert.DoesNotContain(HistoryRow(131), ids);
        Assert.Contains(HistoryRow(132), ids);
    }

    [Fact]
    public async Task Up_History_KeepsExactlyTheRealChanges()
    {
        var before = await ReadHistoryIdsAsync();

        await MigrateToAsync(_migration);

        Assert.Equal(10, before.Count);
        Assert.Equal(
            new[] { 104, 105, 106, 107, 108, 132 }.Select(HistoryRow).Order(),
            (await ReadHistoryIdsAsync()).Order());
    }

    [Fact]
    public async Task Up_EditedAtStampedByARefresh_NoEditedTimestampInTheRawEvent_BecomesNull()
    {
        await MigrateToAsync(_migration);

        Assert.Null((await ReadEditedAtAsync())[101]);
    }

    [Fact]
    public async Task Up_EditedAtStampedByARefresh_BecomesTheEditedTimestampOfTheLatestRawMessageUpdated()
    {
        await MigrateToAsync(_migration);

        Assert.Equal(new DateTime(2023, 5, 31, 21, 36, 28, 654, DateTimeKind.Utc), (await ReadEditedAtAsync())[102]);
    }

    [Fact]
    public async Task Up_EditedAtStampedByARefresh_NoRawMessageUpdated_BecomesNull()
    {
        await MigrateToAsync(_migration);

        Assert.Null((await ReadEditedAtAsync())[109]);
    }

    // 103 also has a refresh row, at an earlier time: its edited_at_utc is from the real edit.
    [Fact]
    public async Task Up_EditedAtOfARealEdit_IsKept()
    {
        var before = await ReadEditedAtAsync();

        await MigrateToAsync(_migration);

        var after = await ReadEditedAtAsync();
        Assert.Equal(new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc), after[103]);
        Assert.All(new long[] { 103, 104, 105, 106, 107, 108 }, id => Assert.Equal(before[id], after[id]));
    }

    private static Guid HistoryRow(int n) => Guid.Parse($"00000000-0000-0000-0001-{n:D12}");

    private static async Task<SeededTemplate> CreateSeededTemplateAsync()
    {
        var connectionString = await PostgresFixture.CreateDatabaseAsync();

        await using var db = NewContext(connectionString);
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(m => m.EndsWith("_CleanupFalseMessageEditHistory", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
        await ExecuteAsync(connectionString, SeedSql);

        // The target is pinned to this migration: a later one may reshape these tables again.
        return new SeededTemplate(new NpgsqlConnectionStringBuilder(connectionString).Database!, migrations[index]);
    }

    private async Task MigrateToAsync(string targetMigration)
    {
        await using var db = NewContext(_connectionString);
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private Task<List<Guid>> ReadHistoryIdsAsync() => QueryAsync(
        "SELECT id FROM message_edit_history", reader => reader.GetGuid(0));

    private async Task<Dictionary<long, DateTime?>> ReadEditedAtAsync()
    {
        var rows = await QueryAsync(
            "SELECT discord_id, edited_at_utc FROM messages",
            reader => (Id: reader.GetInt64(0), EditedAt: reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1)));
        return rows.ToDictionary(r => r.Id, r => r.EditedAt);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
            rows.Add(read(reader));
        return rows;
    }

    private static DiscordDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    private sealed record SeededTemplate(string Database, string Migration);
}
