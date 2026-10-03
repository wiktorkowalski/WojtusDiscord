using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

// The data path of MemeMetadataV2 (#368). Every other class starts from the migrated template,
// where this migration only ever saw an empty meme_annotations. Here it runs over annotations in
// the #367 shape. All reads and writes are raw SQL: the EF model fits the newest schema only.
public sealed class MemeMetadataV2MigrationTests : IAsyncLifetime
{
    private static readonly string[] SchemaV2OnlyColumns =
    [
        "templates", "search_phrases", "people", "people_names", "image_kind", "language", "franchise"
    ];

    // One annotation per case of the migration's two UPDATEs, each under its own status row:
    // 11 template, no source          15 source 'none'
    // 12 source in another case       16 free-text source that is already a tag
    // 13 blank template, source 'X'   17 source with spaces around it
    // 14 free-text source             18 empty source
    // 19 source is the string 'Null'
    private const string SeedAnnotationsSql = """
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
            file_name, file_size_bytes, status, attempt_count, first_seen_utc, last_updated_utc)
        SELECT m.id, 1, 2, 1001, attachment, 'meme-' || attachment || '.png', 1234, 1, 1, now(), now()
        FROM messages AS m, generate_series(11, 19) AS attachment;

        INSERT INTO meme_annotations (meme_index_id, attachment_discord_id, model_id, prompt_version, reasoning_effort,
            indexed_at_utc, description_pl, description_en, ocr_text, tags, source, template, raw_response_json,
            first_seen_utc, last_updated_utc)
        SELECT i.id, v.attachment, 'google/gemini-3-flash-preview', v.prompt_version, v.reasoning_effort,
            '2026-01-02 03:04:05+00', v.description_pl, v.description_en, v.ocr_text, v.tags, v.source, v.template,
            v.raw_response_json::jsonb, '2026-01-02 03:04:05+00', '2026-01-02 03:04:05+00'
        FROM (VALUES
            (11, 'legacy', NULL, 'Raper odrzuca jedno i wybiera drugie', 'A rapper rejects one thing and picks the second',
             'tak / nie', ARRAY['mem', 'wybór'], NULL, 'drake', '{"template": "drake"}'),
            (12, 'other-prompt', 'low', 'Zrzut ekranu wpisu o poniedziałku', 'A screenshot of a post about Monday',
             'nie lubię poniedziałków', ARRAY['wpis', 'poniedziałek'], 'Twitter', NULL, '{"source": "Twitter"}'),
            (13, 'legacy', NULL, 'Zrzut ekranu krótkiego wpisu', 'A screenshot of a short post',
             '', ARRAY['zrzut'], 'X', '  ', '{"source": "X", "template": "  "}'),
            (14, 'other-prompt', NULL, 'Pasek informacyjny z telewizji', 'A news ticker from television',
             'PILNE', ARRAY['pasek', 'wiadomości'], 'TVP Info', NULL, '{"source": "TVP Info"}'),
            (15, 'legacy', NULL, 'Kot śpi na kanapie', 'A cat sleeps on a couch',
             '', ARRAY['kot'], 'none', NULL, '{"source": "none"}'),
            (16, 'other-prompt', NULL, 'Chłopak ogląda się za dziewczyną', 'A boyfriend looks back at a girl',
             '', ARRAY['wykop.pl', 'mem'], 'Wykop.pl', 'Distracted Boyfriend', '{"source": "Wykop.pl"}'),
            (17, 'other-prompt', NULL, 'Pies w okularach', 'A dog in glasses',
             '', ARRAY['pies'], '  Reddit ', NULL, '{"source": "  Reddit "}'),
            (18, 'other-prompt', NULL, 'Żółw na deskorolce', 'A turtle on a skateboard',
             '', ARRAY['żółw'], '', NULL, '{"source": ""}'),
            (19, 'legacy', NULL, 'Ptak na parapecie', 'A bird on a windowsill',
             '', ARRAY['ptak'], 'Null', NULL, '{"source": "null"}')
        ) AS v(attachment, prompt_version, reasoning_effort, description_pl, description_en, ocr_text, tags, source, template, raw_response_json)
        JOIN meme_index AS i ON i.attachment_discord_id = v.attachment;
        """;

    // One replay for the class: a seeded database at AddMemeAnnotations, the migration before
    // this one. Every test changes the schema, so each one works on its own clone of it.
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
    public async Task Up_Template_BecomesAOneElementTemplatesList()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Equal(["drake"], rows[11].Templates);
        Assert.Equal(["Distracted Boyfriend"], rows[16].Templates);
    }

    [Fact]
    public async Task Up_NullOrBlankTemplate_BecomesAnEmptyTemplatesList()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Empty(rows[12].Templates);
        Assert.Empty(rows[13].Templates);
    }

    // The variants the closed set exists to merge: case, spaces, and "x" for twitter. "none", the
    // string "null" (old models wrote it) and an empty text mean no platform; free text becomes
    // the "other" bucket.
    [Fact]
    public async Task Up_LegacySource_IsMappedOntoTheClosedSet()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Equal(
            [(11, null), (12, "twitter"), (13, "twitter"), (14, "other"), (15, null), (16, "other"), (17, "reddit"), (18, null), (19, null)],
            rows.Values.Select(r => ((int)r.AttachmentDiscordId, r.Source)));
    }

    // 'other' is left out of the search columns, so without this the row would lose the words
    // it was found by.
    [Fact]
    public async Task Up_FreeTextSource_IsAddedToTheTags()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Equal(["pasek", "wiadomości", "tvp info"], rows[14].Tags);
    }

    [Fact]
    public async Task Up_FreeTextSourceThatIsAlreadyATag_IsNotAddedTwice()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Equal(["wykop.pl", "mem"], rows[16].Tags);
    }

    [Fact]
    public async Task Up_SourceOfTheClosedSetOrNoSource_LeavesTheTagsAlone()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Equal(["mem", "wybór"], rows[11].Tags);
        Assert.Equal(["wpis", "poniedziałek"], rows[12].Tags);
        Assert.Equal(["zrzut"], rows[13].Tags);
        Assert.Equal(["kot"], rows[15].Tags);
        Assert.Equal(["pies"], rows[17].Tags);
        Assert.Equal(["żółw"], rows[18].Tags);
        Assert.Equal(["ptak"], rows[19].Tags);
    }

    // Unknown, not "other" / "none": nobody looked at these images with the v2 questions.
    [Fact]
    public async Task Up_EveryRow_GetsNullImageKindAndLanguageAndEmptyPeopleAndSearchPhrases()
    {
        await MigrateToAsync(_migration);

        var rows = await ReadRowsAsync();
        Assert.Equal(9, rows.Count);
        Assert.All(rows.Values, r =>
        {
            Assert.Null(r.ImageKind);
            Assert.Null(r.Language);
            Assert.Null(r.Franchise);
            Assert.Equal("[]", r.People);
            Assert.Empty(r.PeopleNames);
            Assert.Empty(r.SearchPhrases);
        });
    }

    [Fact]
    public async Task Up_ColumnsTheMigrationDoesNotMap_AreUnchanged()
    {
        var before = await ReadUnmappedColumnsAsync();

        await MigrateToAsync(_migration);

        Assert.Equal(9, before.Count);
        Assert.Equal(before, await ReadUnmappedColumnsAsync());
    }

    [Fact]
    public async Task Up_MemeAnnotations_LosesTemplateAndGainsTheSchemaV2Columns()
    {
        // Read first: a misspelled name would make the asserts after Up pass on nothing.
        var before = await ReadColumnNamesAsync();

        await MigrateToAsync(_migration);

        var after = await ReadColumnNamesAsync();
        Assert.Contains("template", before);
        Assert.Empty(SchemaV2OnlyColumns.Intersect(before));
        Assert.DoesNotContain("template", after);
        Assert.Empty(SchemaV2OnlyColumns.Except(after));
    }

    // Postgres drops an index with its column, and the generated columns are dropped and re-added.
    [Fact]
    public async Task Up_SearchIndexes_AreBothGinAgain()
    {
        await MigrateToAsync(_migration);

        var definitions = await ReadIndexDefinitionsAsync();
        Assert.Contains("USING gin (search_vector)", definitions["ix_meme_annotations_search_vector"]);
        Assert.Contains("USING gin (search_text gin_trgm_ops)", definitions["ix_meme_annotations_search_text"]);
    }

    [Fact]
    public async Task Up_SourceCheckConstraint_RejectsAValueOutsideTheClosedSet()
    {
        await MigrateToAsync(_migration);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            _connectionString, "UPDATE meme_annotations SET source = 'Twitter' WHERE attachment_discord_id = 11"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_meme_annotations_source", ex.ConstraintName);
    }

    [Theory]
    [InlineData("drake", new[] { 11L })]
    [InlineData("tvp", new[] { 14L })]
    [InlineData("twitter", new[] { 12L, 13L })]
    [InlineData("distracted", new[] { 16L })]
    public async Task Up_MigratedRows_AreFoundThroughTheirSearchVector(string query, long[] expected)
    {
        await MigrateToAsync(_migration);

        Assert.Equal(expected, await SearchByVectorAsync(query));
    }

    // Rows 14 and 16 hold 'other'. It is a bucket, so the vector must not carry it.
    [Fact]
    public async Task Up_SourceOther_IsNotInTheSearchVector()
    {
        await MigrateToAsync(_migration);

        Assert.Empty(await SearchByVectorAsync("other"));
    }

    [Fact]
    public async Task Down_FirstTemplate_ComesBackAsTemplate()
    {
        await MigrateToAsync(_migration);

        await MigrateToAsync(_before);

        var templates = await QueryAsync(
            "SELECT attachment_discord_id, template FROM meme_annotations ORDER BY attachment_discord_id",
            reader => (reader.GetInt64(0), NullableString(reader, 1)));
        // 13 held a blank template: Up turned it into no template, and that is what comes back.
        Assert.Equal(
            [(11L, "drake"), (12L, null), (13L, null), (14L, null), (15L, null), (16L, "Distracted Boyfriend"), (17L, null), (18L, null), (19L, null)],
            templates);
    }

    [Fact]
    public async Task Down_MemeAnnotations_LosesTheSchemaV2ColumnsAndTheSourceConstraint()
    {
        await MigrateToAsync(_migration);

        await MigrateToAsync(_before);

        Assert.Empty(SchemaV2OnlyColumns.Intersect(await ReadColumnNamesAsync()));
        var constraints = await QueryAsync(
            "SELECT conname FROM pg_constraint WHERE conname = 'ck_meme_annotations_source'", reader => reader.GetString(0));
        Assert.Empty(constraints);
    }

    [Fact]
    public async Task Down_SearchIndexes_AreBothGinAgain()
    {
        await MigrateToAsync(_migration);

        await MigrateToAsync(_before);

        var definitions = await ReadIndexDefinitionsAsync();
        Assert.Contains("USING gin (search_vector)", definitions["ix_meme_annotations_search_vector"]);
        Assert.Contains("USING gin (search_text gin_trgm_ops)", definitions["ix_meme_annotations_search_text"]);
    }

    // The old generated SQL is back: it reads template again.
    [Fact]
    public async Task Down_RestoredTemplate_IsFoundThroughTheSearchVector()
    {
        await MigrateToAsync(_migration);

        await MigrateToAsync(_before);

        Assert.Equal([11L], await SearchByVectorAsync("drake"));
    }

    // Down is lossy for the source: the mapped value stays, and so does the tag Up added.
    [Fact]
    public async Task Down_MappedSourceAndAddedTag_Stay()
    {
        await MigrateToAsync(_migration);

        await MigrateToAsync(_before);

        var rows = await QueryAsync(
            "SELECT attachment_discord_id, source, tags FROM meme_annotations WHERE attachment_discord_id IN (12, 14) ORDER BY 1",
            reader => (Attachment: reader.GetInt64(0), Source: reader.GetString(1), Tags: reader.GetFieldValue<string[]>(2)));
        Assert.Equal([(12L, "twitter"), (14L, "other")], rows.Select(r => (r.Attachment, r.Source)));
        Assert.Equal(["pasek", "wiadomości", "tvp info"], rows[1].Tags);
    }

    [Fact]
    public async Task DownThenUp_Rows_EndInTheSameStateAsAfterTheFirstUp()
    {
        await MigrateToAsync(_migration);
        var afterFirstUp = await ReadRowsAsync();

        await MigrateToAsync(_before);
        await MigrateToAsync(_migration);

        var afterSecondUp = await ReadRowsAsync();
        Assert.Equal(["drake"], afterSecondUp[11].Templates);
        Assert.Equal(afterFirstUp.Keys, afterSecondUp.Keys);
        Assert.All(afterFirstUp, pair => AssertSameRow(pair.Value, afterSecondUp[pair.Key]));
    }

    private static void AssertSameRow(AnnotationRow expected, AnnotationRow actual)
    {
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.Templates, actual.Templates);
        Assert.Equal(expected.SearchPhrases, actual.SearchPhrases);
        Assert.Equal(expected.People, actual.People);
        Assert.Equal(expected.PeopleNames, actual.PeopleNames);
        Assert.Equal((expected.ImageKind, expected.Language, expected.Franchise), (actual.ImageKind, actual.Language, actual.Franchise));
    }

    private static async Task<SeededTemplate> CreateSeededTemplateAsync()
    {
        var connectionString = await PostgresFixture.CreateDatabaseAsync();

        await using var db = NewContext(connectionString);
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(m => m.EndsWith("_MemeMetadataV2", StringComparison.Ordinal));
        var before = migrations[index - 1];
        await db.GetService<IMigrator>().MigrateAsync(before);
        await ExecuteAsync(connectionString, SeedAnnotationsSql);

        // The target is pinned to this migration, not to the newest one: a later migration may
        // reshape meme_annotations again, and these asserts are about this step only.
        return new SeededTemplate(new NpgsqlConnectionStringBuilder(connectionString).Database!, before, migrations[index]);
    }

    private async Task MigrateToAsync(string targetMigration)
    {
        await using var db = NewContext(_connectionString);
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task<SortedDictionary<long, AnnotationRow>> ReadRowsAsync()
    {
        var rows = await QueryAsync(
            """
            SELECT attachment_discord_id, source, tags, templates, search_phrases, people::text, people_names,
                   image_kind, language, franchise
            FROM meme_annotations
            """,
            reader => new AnnotationRow(
                reader.GetInt64(0), NullableString(reader, 1), reader.GetFieldValue<string[]>(2),
                reader.GetFieldValue<string[]>(3), reader.GetFieldValue<string[]>(4), reader.GetString(5),
                reader.GetFieldValue<string[]>(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                NullableString(reader, 9)));
        return new SortedDictionary<long, AnnotationRow>(rows.ToDictionary(r => r.AttachmentDiscordId));
    }

    // Each row as one text value: the columns that exist on both sides of the migration and that
    // it has no reason to touch.
    private Task<List<string>> ReadUnmappedColumnsAsync() => QueryAsync(
        """
        SELECT ROW(id, meme_index_id, attachment_discord_id, model_id, prompt_version, reasoning_effort, indexed_at_utc,
                   description_pl, description_en, ocr_text, raw_response_json, first_seen_utc, last_updated_utc)::text
        FROM meme_annotations
        ORDER BY attachment_discord_id
        """,
        reader => reader.GetString(0));

    private Task<List<string>> ReadColumnNamesAsync() => QueryAsync(
        "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'meme_annotations'",
        reader => reader.GetString(0));

    private async Task<Dictionary<string, string>> ReadIndexDefinitionsAsync()
    {
        var indexes = await QueryAsync(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'meme_annotations'",
            reader => (Name: reader.GetString(0), Definition: reader.GetString(1)));
        return indexes.ToDictionary(i => i.Name, i => i.Definition);
    }

    private Task<List<long>> SearchByVectorAsync(string query) => QueryAsync(
        $"""
        SELECT attachment_discord_id FROM meme_annotations
        WHERE search_vector @@ websearch_to_tsquery('simple', public.f_unaccent('{query}'))
        ORDER BY attachment_discord_id
        """,
        reader => reader.GetInt64(0));

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

    private sealed record AnnotationRow(
        long AttachmentDiscordId,
        string? Source,
        string[] Tags,
        string[] Templates,
        string[] SearchPhrases,
        string People,
        string[] PeopleNames,
        int? ImageKind,
        int? Language,
        string? Franchise);
}
