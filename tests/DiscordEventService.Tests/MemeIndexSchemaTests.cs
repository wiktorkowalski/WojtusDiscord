using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeIndexSchemaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Mirrors the production trigram threshold in MemeSearchService.
    private const double TrigramSimilarityThreshold = 0.4;

    private const string DefaultModel = "google/gemini-3-flash-preview";

    private DiscordDbContext _db = null!;
    private MessageEntity _liveMessage = null!;
    private MessageEntity _deletedMessage = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeAnnotations.ExecuteDeleteAsync();
        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = 1UL, Name = "g" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();

        var channel = new ChannelEntity { DiscordId = 2UL, GuildId = guild.Id, Name = "memes", Type = ChannelType.Text };
        var author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(channel);
        _db.Users.Add(author);
        await _db.SaveChangesAsync();

        _liveMessage = Message(channel, author, guild, 1001UL, isDeleted: false);
        _deletedMessage = Message(channel, author, guild, 1002UL, isDeleted: true);
        _db.Messages.AddRange(_liveMessage, _deletedMessage);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Insert_WhenDuplicateAttachmentDiscordId_IsRejected()
    {
        _db.MemeIndex.Add(PendingMeme(42UL));
        await _db.SaveChangesAsync();

        await using var second = NewContext();
        second.MemeIndex.Add(PendingMeme(42UL));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    // No CHECK can span meme_index and meme_annotations: "Indexed has an annotation" is the
    // writers' rule (one SaveChanges), so the database itself must take this row.
    [Fact]
    public async Task Insert_WhenIndexedWithoutAnnotation_IsAcceptedByTheDatabase()
    {
        _db.MemeIndex.Add(IndexedMeme(43UL));
        await _db.SaveChangesAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 43UL);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
    }

    [Theory]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Skipped)]
    public async Task Insert_WhenFailedOrSkippedWithoutError_ViolatesStatusConstraint(MemeIndexStatus status)
    {
        var meme = PendingMeme(44UL);
        meme.Status = status;
        _db.MemeIndex.Add(meme);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        Assert.Equal("ck_meme_index_status", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
    }

    [Fact]
    public async Task Insert_WhenPending_GeneratesId()
    {
        _db.MemeIndex.Add(PendingMeme(45UL));
        await _db.SaveChangesAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 45UL);
        Assert.NotEqual(Guid.Empty, row.Id);
    }

    [Fact]
    public async Task InsertAnnotation_GeneratesIdAndSearchColumns()
    {
        var meme = IndexedMeme(46UL);
        meme.Annotations.Add(Annotation(46UL, "Pies siedzi przy komputerze", "A dog sits at a computer", "", ["pies"]));
        _db.MemeIndex.Add(meme);
        await _db.SaveChangesAsync();

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 46UL);
        Assert.NotEqual(Guid.Empty, annotation.Id);
        Assert.Equal(meme.Id, annotation.MemeIndexId);
        Assert.Contains("pies", annotation.SearchText);
        Assert.NotNull(annotation.SearchVector);
    }

    [Fact]
    public async Task InsertAnnotation_WhenSameAttachmentModelAndPromptVersion_IsRejected()
    {
        var meme = IndexedMeme(47UL);
        meme.Annotations.Add(Annotation(47UL, "Pierwszy opis", "First", "", ["pierwszy"]));
        _db.MemeIndex.Add(meme);
        await _db.SaveChangesAsync();

        await using var second = NewContext();
        var duplicate = Annotation(47UL, "Drugi opis", "Second", "", ["drugi"]);
        duplicate.MemeIndexId = meme.Id;
        second.MemeAnnotations.Add(duplicate);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        Assert.Equal("ix_meme_annotations_key", Assert.IsType<PostgresException>(ex.InnerException).ConstraintName);
    }

    [Theory]
    [InlineData("other/model", null)]
    [InlineData(null, "other-prompt")]
    public async Task InsertAnnotation_WhenOtherModelOrOtherPromptVersion_Persists(string? otherModel, string? otherPromptVersion)
    {
        var meme = IndexedMeme(48UL);
        meme.Annotations.Add(Annotation(48UL, "Pierwszy opis", "First", "", ["pierwszy"]));
        _db.MemeIndex.Add(meme);
        await _db.SaveChangesAsync();

        await using var second = NewContext();
        var other = Annotation(48UL, "Drugi opis", "Second", "", ["drugi"],
            modelId: otherModel ?? DefaultModel,
            promptVersion: otherPromptVersion ?? OpenRouterClient.PromptVersion);
        other.MemeIndexId = meme.Id;
        second.MemeAnnotations.Add(other);
        await second.SaveChangesAsync();

        await using var verify = NewContext();
        Assert.Equal(2, await verify.MemeAnnotations.CountAsync(a => a.AttachmentDiscordId == 48UL));
    }

    [Fact]
    public async Task Delete_WhenStatusRowHasAnnotations_IsRejected()
    {
        var meme = IndexedMeme(49UL);
        meme.Annotations.Add(Annotation(49UL, "Opis", "Description", "", ["tag"]));
        _db.MemeIndex.Add(meme);
        await _db.SaveChangesAsync();

        // A stub with no loaded annotations: EF sends the bare DELETE and the FK has to refuse it.
        await using var second = NewContext();
        second.MemeIndex.Remove(new MemeIndexEntity { Id = meme.Id });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // 23001, not 23503: the FK is ON DELETE RESTRICT, not NO ACTION.
        Assert.Equal(PostgresErrorCodes.RestrictViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        await using var verify = NewContext();
        Assert.Equal(1, await verify.MemeIndex.CountAsync(m => m.AttachmentDiscordId == 49UL));
        Assert.Equal(1, await verify.MemeAnnotations.CountAsync(a => a.AttachmentDiscordId == 49UL));
    }

    [Fact]
    public async Task SearchIndexes_OnMemeAnnotations_AreBothGin()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexname, indexdef FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'meme_annotations'
            """;
        var definitions = new Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                definitions[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.Contains("USING gin (search_vector)", definitions["ix_meme_annotations_search_vector"]);
        Assert.Contains("USING gin (search_text gin_trgm_ops)", definitions["ix_meme_annotations_search_text"]);
    }

    [Fact]
    public async Task SearchVector_MultiWordAccentInsensitiveQuery_FindsOnlyMatchingRow()
    {
        var dog = IndexedMeme(50UL);
        dog.Annotations.Add(Annotation(50UL,
            descriptionPl: "Pies siedzi przy komputerze",
            descriptionEn: "A dog sits at a computer",
            ocrText: "kiedy kod działa za pierwszym razem",
            tags: ["pies", "programowanie"]));
        var cat = IndexedMeme(51UL);
        cat.Annotations.Add(Annotation(51UL,
            descriptionPl: "Kot patrzy na lodówkę",
            descriptionEn: "A cat stares at the fridge",
            ocrText: "",
            tags: ["kot"]));
        _db.MemeIndex.AddRange(dog, cat);
        await _db.SaveChangesAsync();

        // Accentless multi-word query must hit the accented OCR text ("działa").
        var hits = await SearchByVectorAsync("dziala kod");

        Assert.Equal([50L], hits);
    }

    [Fact]
    public async Task SearchText_TrigramSimilarity_MatchesPolishInflectionBothWays()
    {
        var inflected = IndexedMeme(60UL);
        inflected.Annotations.Add(Annotation(60UL,
            descriptionPl: "Mem o bazie danych",
            descriptionEn: "Database meme",
            ocrText: "",
            tags: ["postgresie"]));
        var baseForm = IndexedMeme(61UL);
        baseForm.Annotations.Add(Annotation(61UL,
            descriptionPl: "Inny mem o bazie danych",
            descriptionEn: "Another database meme",
            ocrText: "",
            tags: ["postgres"]));
        _db.MemeIndex.AddRange(inflected, baseForm);
        await _db.SaveChangesAsync();

        var forBaseForm = await SearchByTrigramAsync("postgres");
        var forInflected = await SearchByTrigramAsync("postgresie");

        Assert.Contains(60L, forBaseForm);
        Assert.Contains(61L, forInflected);
    }

    [Fact]
    public async Task MessagesJoin_WhenMessageSoftDeleted_RowIsExcludable()
    {
        _db.MemeIndex.Add(PendingMeme(70UL));
        var onDeleted = PendingMeme(71UL);
        onDeleted.MessageId = _deletedMessage.Id;
        onDeleted.MessageDiscordId = _deletedMessage.DiscordId;
        _db.MemeIndex.Add(onDeleted);
        await _db.SaveChangesAsync();

        // The §6 query shape: search hits joined to messages, soft-deleted out.
        var visible = await _db.MemeIndex
            .Where(m => !m.Message.IsDeleted)
            .Select(m => m.AttachmentDiscordId)
            .ToListAsync();

        Assert.Equal([70UL], visible);
    }

    private async Task<List<long>> SearchByVectorAsync(string query)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Raw SQL: LINQ cannot express the custom public.f_unaccent function or
        // websearch_to_tsquery; this pins the migration-created FTS schema.
        command.CommandText = """
            SELECT attachment_discord_id FROM meme_annotations
            WHERE search_vector @@ websearch_to_tsquery('simple', public.f_unaccent($1))
            ORDER BY attachment_discord_id
            """;
        command.Parameters.AddWithValue(query);
        return await ReadIdsAsync(command);
    }

    private async Task<List<long>> SearchByTrigramAsync(string query)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Raw SQL: LINQ cannot express the custom public.f_unaccent / word_similarity
        // trigram functions; this pins the migration-created trigram schema.
        command.CommandText = """
            SELECT attachment_discord_id FROM meme_annotations
            WHERE word_similarity(public.f_unaccent($1), search_text) >= $2
            ORDER BY attachment_discord_id
            """;
        command.Parameters.AddWithValue(query);
        command.Parameters.AddWithValue(TrigramSimilarityThreshold);
        return await ReadIdsAsync(command);
    }

    private static async Task<List<long>> ReadIdsAsync(NpgsqlCommand command)
    {
        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    private MemeIndexEntity PendingMeme(ulong attachmentDiscordId) => new MemeIndexEntity
    {
        MessageId = _liveMessage.Id,
        GuildDiscordId = 1UL,
        ChannelDiscordId = 2UL,
        MessageDiscordId = _liveMessage.DiscordId,
        AttachmentDiscordId = attachmentDiscordId,
        FileName = "meme.png",
        FileSizeBytes = 1234,
        ContentType = "image/png",
        Status = MemeIndexStatus.Pending
    };

    private MemeIndexEntity IndexedMeme(ulong attachmentDiscordId)
    {
        var meme = PendingMeme(attachmentDiscordId);
        meme.Status = MemeIndexStatus.Indexed;
        meme.ContentHash = $"hash-{attachmentDiscordId}";
        return meme;
    }

    private static MemeAnnotationEntity Annotation(
        ulong attachmentDiscordId,
        string descriptionPl,
        string descriptionEn,
        string ocrText,
        string[] tags,
        string modelId = DefaultModel,
        string promptVersion = OpenRouterClient.PromptVersion) => new MemeAnnotationEntity
        {
            AttachmentDiscordId = attachmentDiscordId,
            ModelId = modelId,
            PromptVersion = promptVersion,
            IndexedAtUtc = DateTime.UtcNow,
            DescriptionPl = descriptionPl,
            DescriptionEn = descriptionEn,
            OcrText = ocrText,
            Tags = tags,
            RawResponseJson = "{}"
        };

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    private MessageEntity Message(ChannelEntity channel, UserEntity author, GuildEntity guild, ulong discordId, bool isDeleted) =>
        new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = channel.Id,
            GuildId = guild.Id,
            AuthorId = author.Id,
            HasAttachments = true,
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? DateTime.UtcNow : null
        };
}
