using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

// Every other class starts from the migrated template, where AddMemeAnnotations only ever saw an
// empty meme_index. Here it runs over rows. All reads and writes are raw SQL: the EF model fits
// the newest schema only, and these tests stand on the two schemas around this one migration.
public sealed class MemeAnnotationsMigrationTests : IAsyncLifetime
{
    private const string IndexedRowId = "00000000-0000-0000-0000-000000000011";
    private const string LegacyModel = "google/gemini-3-flash-preview";
    private static readonly DateTime LegacyIndexedAtUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static readonly string[] MovedColumns =
    [
        "description_pl", "description_en", "ocr_text", "tags", "source", "template",
        "model_id", "raw_response_json", "indexed_at_utc", "search_vector", "search_text"
    ];

    private static readonly StatusRow[] SeededStatusRows =
    [
        new(11L, (int)MemeIndexStatus.Indexed, null, 1, "hash-11"),
        new(12L, (int)MemeIndexStatus.Pending, null, 0, null),
        new(13L, (int)MemeIndexStatus.Failed, "model exploded", 2, "hash-13"),
        new(14L, (int)MemeIndexStatus.Skipped, "dead attachment: download HTTP 404", 1, null)
    ];

    // One Indexed row with a full metadata block, and one row for each other status.
    // status holds the persisted numbers: 0 Pending, 1 Indexed, 2 Failed, 3 Skipped.
    private const string SeedOldShapeSql = $$"""
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

        INSERT INTO meme_index (id, message_id, guild_discord_id, channel_discord_id, message_discord_id, attachment_discord_id,
            file_name, file_size_bytes, content_hash, status, error, attempt_count,
            description_pl, description_en, ocr_text, tags, source, template, model_id, raw_response_json, indexed_at_utc,
            first_seen_utc, last_updated_utc)
        VALUES
            ('{{IndexedRowId}}', '00000000-0000-0000-0000-000000000004', 1, 2, 1001, 11,
             'indexed.png', 1234, 'hash-11', 1, NULL, 1,
             'Żółw jedzie na deskorolce', 'A turtle rides a skateboard', 'kiedy kod działa', '{"żółw","deskorolka"}',
             'reddit', 'Distracted Boyfriend', '{{LegacyModel}}', '{"ok": true}', '2026-01-02 03:04:05+00',
             now(), now()),
            (uuidv7(), '00000000-0000-0000-0000-000000000004', 1, 2, 1001, 12,
             'pending.png', 1234, NULL, 0, NULL, 0,
             NULL, NULL, NULL, '{}', NULL, NULL, NULL, NULL, NULL, now(), now()),
            (uuidv7(), '00000000-0000-0000-0000-000000000004', 1, 2, 1001, 13,
             'failed.png', 1234, 'hash-13', 2, 'model exploded', 2,
             NULL, NULL, NULL, '{}', NULL, NULL, NULL, NULL, NULL, now(), now()),
            (uuidv7(), '00000000-0000-0000-0000-000000000004', 1, 2, 1001, 14,
             'skipped.png', 1234, NULL, 3, 'dead attachment: download HTTP 404', 1,
             NULL, NULL, NULL, '{}', NULL, NULL, NULL, NULL, NULL, now(), now());
        """;

    // One replay for the class: a seeded database at the migration before AddMemeAnnotations.
    // Every test changes the schema, so each one works on its own clone of it.
    private static readonly Lazy<Task<OldShapeTemplate>> Template = new(CreateOldShapeTemplateAsync);

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
    public async Task Up_IndexedRow_BecomesOneLegacyAnnotationWithAllItsMetadata()
    {
        await MigrateToAsync(_migration);

        var annotation = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal(Guid.Parse(IndexedRowId), annotation.MemeIndexId);
        Assert.Equal(11L, annotation.AttachmentDiscordId);
        Assert.Equal(LegacyModel, annotation.ModelId);
        Assert.Equal("legacy", annotation.PromptVersion);
        Assert.Null(annotation.ReasoningEffort);
        Assert.Equal(LegacyIndexedAtUtc, annotation.IndexedAtUtc);
        Assert.Equal("Żółw jedzie na deskorolce", annotation.DescriptionPl);
        Assert.Equal("A turtle rides a skateboard", annotation.DescriptionEn);
        Assert.Equal("kiedy kod działa", annotation.OcrText);
        Assert.Equal(["żółw", "deskorolka"], annotation.Tags);
        Assert.Equal("reddit", annotation.Source);
        Assert.Equal("Distracted Boyfriend", annotation.Template);
        Assert.Equal("""{"ok": true}""", annotation.RawResponseJson);
    }

    [Fact]
    public async Task Up_StatusRows_KeepStatusErrorAttemptCountAndContentHash()
    {
        await MigrateToAsync(_migration);

        Assert.Equal(SeededStatusRows, await ReadStatusRowsAsync());
    }

    [Fact]
    public async Task Up_MemeIndex_NoLongerHasTheMetadataAndSearchColumns()
    {
        // Read first: a misspelled name in MovedColumns would make the assert after Up pass on nothing.
        var before = await ReadColumnNamesAsync("meme_index");

        await MigrateToAsync(_migration);

        var after = await ReadColumnNamesAsync("meme_index");
        Assert.Empty(MovedColumns.Except(before));
        Assert.Empty(MovedColumns.Intersect(after));
    }

    [Fact]
    public async Task Up_MigratedAnnotation_IsFoundThroughItsSearchVector()
    {
        await MigrateToAsync(_migration);

        var hits = await QueryAsync(
            """
            SELECT attachment_discord_id FROM meme_annotations
            WHERE search_vector @@ websearch_to_tsquery('simple', public.f_unaccent('zolw'))
            """,
            reader => reader.GetInt64(0));

        Assert.Equal([11L], hits);
    }

    [Fact]
    public async Task DownThenUp_IndexedRow_GetsItsMetadataBackAndMovesItAgain()
    {
        await MigrateToAsync(_migration);

        await MigrateToAsync(_before);

        var restored = Assert.Single(await ReadOldShapeRowsAsync(), r => r.AttachmentDiscordId == 11L);
        Assert.Equal((int)MemeIndexStatus.Indexed, restored.Status);
        Assert.Equal(LegacyModel, restored.ModelId);
        Assert.Equal(LegacyIndexedAtUtc, restored.IndexedAtUtc);
        Assert.Equal("Żółw jedzie na deskorolce", restored.DescriptionPl);
        Assert.Equal("A turtle rides a skateboard", restored.DescriptionEn);
        Assert.Equal("kiedy kod działa", restored.OcrText);
        Assert.Equal(["żółw", "deskorolka"], restored.Tags);
        Assert.Equal("reddit", restored.Source);
        Assert.Equal("Distracted Boyfriend", restored.Template);
        Assert.Equal("""{"ok": true}""", restored.RawResponseJson);
        Assert.Equal(SeededStatusRows, await ReadStatusRowsAsync());

        await MigrateToAsync(_migration);

        var annotation = Assert.Single(await ReadAnnotationsAsync());
        Assert.Equal((11L, "legacy", "Żółw jedzie na deskorolce"),
            (annotation.AttachmentDiscordId, annotation.PromptVersion, annotation.DescriptionPl));
    }

    // meme_index holds one metadata block per attachment, so Down can bring back only one annotation.
    [Fact]
    public async Task Down_IndexedRowWithTwoAnnotations_GetsTheNewestBack()
    {
        await MigrateToAsync(_migration);
        await ExecuteAsync(_connectionString, $$"""
            INSERT INTO meme_annotations (meme_index_id, attachment_discord_id, model_id, prompt_version, indexed_at_utc,
                description_pl, description_en, ocr_text, tags, raw_response_json, first_seen_utc, last_updated_utc)
            VALUES ('{{IndexedRowId}}', 11, 'other/model', 'other-prompt', '2026-02-03 04:05:06+00',
                'Nowszy opis', 'Newer description', '', '{"nowszy"}', NULL, now(), now());
            """);

        await MigrateToAsync(_before);

        var restored = Assert.Single(await ReadOldShapeRowsAsync(), r => r.AttachmentDiscordId == 11L);
        Assert.Equal(("other/model", "Nowszy opis"), (restored.ModelId, restored.DescriptionPl));
        Assert.Equal(new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc), restored.IndexedAtUtc);
        Assert.Equal(["nowszy"], restored.Tags);
        Assert.Null(restored.RawResponseJson);
    }

    // The old check constraint rejects an Indexed row without metadata; the new schema allows one.
    [Fact]
    public async Task Down_IndexedRowWithoutAnnotation_BecomesPending()
    {
        await MigrateToAsync(_migration);
        await ExecuteAsync(_connectionString, """
            INSERT INTO meme_index (message_id, guild_discord_id, channel_discord_id, message_discord_id, attachment_discord_id,
                file_name, file_size_bytes, status, attempt_count, first_seen_utc, last_updated_utc)
            SELECT id, 1, 2, 1001, 15, 'bare.png', 1234, 1, 1, now(), now() FROM messages;
            """);

        await MigrateToAsync(_before);

        var bare = Assert.Single(await ReadOldShapeRowsAsync(), r => r.AttachmentDiscordId == 15L);
        Assert.Equal((int)MemeIndexStatus.Pending, bare.Status);
        Assert.Null(bare.IndexedAtUtc);
    }

    private static async Task<OldShapeTemplate> CreateOldShapeTemplateAsync()
    {
        var connectionString = await PostgresFixture.CreateDatabaseAsync();

        await using var db = NewContext(connectionString);
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(m => m.EndsWith("_AddMemeAnnotations", StringComparison.Ordinal));
        var before = migrations[index - 1];
        await db.GetService<IMigrator>().MigrateAsync(before);
        await ExecuteAsync(connectionString, SeedOldShapeSql);

        // The target is pinned to this migration, not to the newest one: a later migration may
        // reshape meme_annotations again, and these asserts are about this step only.
        return new OldShapeTemplate(new NpgsqlConnectionStringBuilder(connectionString).Database!, before, migrations[index]);
    }

    private async Task MigrateToAsync(string targetMigration)
    {
        await using var db = NewContext(_connectionString);
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private Task<List<AnnotationRow>> ReadAnnotationsAsync() => QueryAsync(
        """
        SELECT meme_index_id, attachment_discord_id, model_id, prompt_version, reasoning_effort, indexed_at_utc,
               description_pl, description_en, ocr_text, tags, source, template, raw_response_json::text
        FROM meme_annotations
        ORDER BY attachment_discord_id, indexed_at_utc
        """,
        reader => new AnnotationRow(
            reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), NullableString(reader, 4),
            reader.GetDateTime(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            reader.GetFieldValue<string[]>(9), NullableString(reader, 10), NullableString(reader, 11), NullableString(reader, 12)));

    private Task<List<StatusRow>> ReadStatusRowsAsync() => QueryAsync(
        """
        SELECT attachment_discord_id, status, error, attempt_count, content_hash
        FROM meme_index
        ORDER BY attachment_discord_id
        """,
        reader => new StatusRow(
            reader.GetInt64(0), reader.GetInt32(1), NullableString(reader, 2), reader.GetInt32(3), NullableString(reader, 4)));

    private Task<List<OldShapeRow>> ReadOldShapeRowsAsync() => QueryAsync(
        """
        SELECT attachment_discord_id, status, model_id, indexed_at_utc, description_pl, description_en, ocr_text,
               tags, source, template, raw_response_json::text
        FROM meme_index
        ORDER BY attachment_discord_id
        """,
        reader => new OldShapeRow(
            reader.GetInt64(0), reader.GetInt32(1), NullableString(reader, 2),
            reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            NullableString(reader, 4), NullableString(reader, 5), NullableString(reader, 6),
            reader.GetFieldValue<string[]>(7), NullableString(reader, 8), NullableString(reader, 9), NullableString(reader, 10)));

    private Task<List<string>> ReadColumnNamesAsync(string table) => QueryAsync(
        $"SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}'",
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

    private sealed record OldShapeTemplate(string Database, string Before, string Migration);

    private sealed record StatusRow(long AttachmentDiscordId, int Status, string? Error, int AttemptCount, string? ContentHash);

    private sealed record AnnotationRow(
        Guid MemeIndexId,
        long AttachmentDiscordId,
        string ModelId,
        string PromptVersion,
        string? ReasoningEffort,
        DateTime IndexedAtUtc,
        string DescriptionPl,
        string DescriptionEn,
        string OcrText,
        string[] Tags,
        string? Source,
        string? Template,
        string? RawResponseJson);

    private sealed record OldShapeRow(
        long AttachmentDiscordId,
        int Status,
        string? ModelId,
        DateTime? IndexedAtUtc,
        string? DescriptionPl,
        string? DescriptionEn,
        string? OcrText,
        string[] Tags,
        string? Source,
        string? Template,
        string? RawResponseJson);
}
