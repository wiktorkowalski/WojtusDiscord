using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

// The data path of MemeIndexRefusalMarker (#373). Every other class starts from the migrated
// template, where this migration only ever saw an empty meme_index. Here it runs over one status
// row of each kind. All reads and writes are raw SQL: the EF model fits the newest schema only.
public sealed class MemeIndexRefusalMarkerMigrationTests : IAsyncLifetime
{
    private static readonly string[] MarkerColumns = ["refused_by_model_id", "refused_by_prompt_version"];

    // 14 is a refusal from before the marker existed: nothing in the row says which model refused.
    private static readonly StatusRow[] SeededStatusRows =
    [
        new(11L, (int)MemeIndexStatus.Indexed, null, 1, "hash-11"),
        new(12L, (int)MemeIndexStatus.Pending, null, 0, null),
        new(13L, (int)MemeIndexStatus.Failed, "model exploded", 2, "hash-13"),
        new(14L, (int)MemeIndexStatus.Skipped, "model refusal: safety", 1, "hash-14"),
        new(15L, (int)MemeIndexStatus.Skipped, "dead attachment: download HTTP 404", 1, null)
    ];

    // status holds the persisted numbers: 0 Pending, 1 Indexed, 2 Failed, 3 Skipped.
    private const string SeedSql = """
        INSERT INTO guilds (id, discord_id, name, owner_id, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000001', 1, 'g', 0, now(), now());

        INSERT INTO channels (id, discord_id, guild_id, name, type, position, is_nsfw, is_deleted, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000002', 2, '00000000-0000-0000-0000-000000000001', 'memes', 0, 0, false, false, now(), now());

        INSERT INTO users (id, discord_id, username, is_bot, is_system, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000003', 3, 'u', false, false, now(), now());

        INSERT INTO messages (id, discord_id, channel_id, guild_id, author_id, created_at_utc, flags,
            has_attachments, has_embeds, is_deleted, first_seen_utc, last_updated_utc)
        VALUES ('00000000-0000-0000-0000-000000000004', 1001, '00000000-0000-0000-0000-000000000002',
            '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000003', now(), 0,
            true, false, false, now(), now());

        INSERT INTO meme_index (message_id, guild_discord_id, channel_discord_id, message_discord_id, attachment_discord_id,
            file_name, file_size_bytes, content_hash, status, error, attempt_count, first_seen_utc, last_updated_utc)
        SELECT m.id, 1, 2, 1001, v.attachment, 'meme-' || v.attachment || '.png', 1234, v.content_hash, v.status, v.error,
            v.attempt_count, now(), now()
        FROM messages AS m, (VALUES
            (11, 1, NULL, 1, 'hash-11'),
            (12, 0, NULL, 0, NULL),
            (13, 2, 'model exploded', 2, 'hash-13'),
            (14, 3, 'model refusal: safety', 1, 'hash-14'),
            (15, 3, 'dead attachment: download HTTP 404', 1, NULL)
        ) AS v(attachment, status, error, attempt_count, content_hash);
        """;

    // One replay for the class: a seeded database at the migration before this one. Every test
    // changes the schema, so each one works on its own clone of it.
    private static readonly Lazy<Task<SeededTemplate>> Template = new(CreateSeededTemplateAsync);

    private string _connectionString = null!;
    private string _before = null!;
    private string _migration = null!;

    public async Task InitializeAsync()
    {
        (var database, _before, _migration) = await Template.Value;
        _connectionString = await PostgresFixture.CreateDatabaseAsync(database);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Up_StatusRows_KeepEveryValueAndGetTwoEmptyMarkerColumns()
    {
        var before = await ReadColumnNamesAsync();

        await MigrateToAsync(_migration);

        var after = await ReadColumnNamesAsync();
        Assert.Equal(MarkerColumns, after.Except(before).Order());
        Assert.Empty(before.Except(after));
        Assert.Equal(SeededStatusRows, await ReadStatusRowsAsync());
        Assert.Equal(0L, Assert.Single(await QueryAsync(
            "SELECT count(*) FROM meme_index WHERE refused_by_model_id IS NOT NULL OR refused_by_prompt_version IS NOT NULL",
            reader => reader.GetInt64(0))));
    }

    // Down drops the marker and nothing else: a refusal is then terminal for every model again.
    [Fact]
    public async Task Down_RowWithAMarker_KeepsItsStatusRowAndLosesOnlyTheMarker()
    {
        await MigrateToAsync(_migration);
        await ExecuteAsync(_connectionString, """
            UPDATE meme_index SET refused_by_model_id = 'some/model', refused_by_prompt_version = 'v4'
            WHERE attachment_discord_id IN (11, 14);
            """);

        await MigrateToAsync(_before);

        Assert.Equal(SeededStatusRows, await ReadStatusRowsAsync());
        Assert.Empty(MarkerColumns.Intersect(await ReadColumnNamesAsync()));
    }

    private static async Task<SeededTemplate> CreateSeededTemplateAsync()
    {
        var connectionString = await PostgresFixture.CreateDatabaseAsync();

        await using var db = NewContext(connectionString);
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(m => m.EndsWith("_MemeIndexRefusalMarker", StringComparison.Ordinal));
        var before = migrations[index - 1];
        await db.GetService<IMigrator>().MigrateAsync(before);
        await ExecuteAsync(connectionString, SeedSql);

        // The target is pinned to this migration, not to the newest one: a later migration may
        // reshape meme_index again, and these asserts are about this step only.
        return new SeededTemplate(new NpgsqlConnectionStringBuilder(connectionString).Database!, before, migrations[index]);
    }

    private async Task MigrateToAsync(string targetMigration)
    {
        await using var db = NewContext(_connectionString);
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private Task<List<StatusRow>> ReadStatusRowsAsync() => QueryAsync(
        """
        SELECT attachment_discord_id, status, error, attempt_count, content_hash
        FROM meme_index
        ORDER BY attachment_discord_id
        """,
        reader => new StatusRow(
            reader.GetInt64(0), reader.GetInt32(1), NullableString(reader, 2), reader.GetInt32(3), NullableString(reader, 4)));

    private Task<List<string>> ReadColumnNamesAsync() => QueryAsync(
        "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'meme_index'",
        reader => reader.GetString(0));

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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

    private sealed record SeededTemplate(string Database, string Before, string Migration);

    private sealed record StatusRow(long AttachmentDiscordId, int Status, string? Error, int AttemptCount, string? ContentHash);
}
