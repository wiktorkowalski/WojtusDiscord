using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeIndexSchemaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Mirrors the production trigram threshold in MemeSearchService.
    private const double TrigramSimilarityThreshold = 0.4;

    private DiscordDbContext _db = null!;
    private MessageEntity _liveMessage = null!;
    private MessageEntity _deletedMessage = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

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

    [Fact]
    public async Task Insert_WhenIndexedWithoutMetadata_ViolatesStatusConstraint()
    {
        var meme = PendingMeme(43UL);
        meme.Status = MemeIndexStatus.Indexed;
        meme.IndexedAtUtc = DateTime.UtcNow;
        _db.MemeIndex.Add(meme);

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Insert_WhenFailedWithoutError_ViolatesStatusConstraint()
    {
        var meme = PendingMeme(44UL);
        meme.Status = MemeIndexStatus.Failed;
        _db.MemeIndex.Add(meme);

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Insert_WhenPending_GeneratesIdAndSearchColumns()
    {
        _db.MemeIndex.Add(PendingMeme(45UL));
        await _db.SaveChangesAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 45UL);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.NotNull(row.SearchText);
    }

    [Fact]
    public async Task SearchVector_MultiWordAccentInsensitiveQuery_FindsOnlyMatchingRow()
    {
        _db.MemeIndex.Add(IndexedMeme(50UL,
            descriptionPl: "Pies siedzi przy komputerze",
            descriptionEn: "A dog sits at a computer",
            ocrText: "kiedy kod działa za pierwszym razem",
            tags: ["pies", "programowanie"]));
        _db.MemeIndex.Add(IndexedMeme(51UL,
            descriptionPl: "Kot patrzy na lodówkę",
            descriptionEn: "A cat stares at the fridge",
            ocrText: "",
            tags: ["kot"]));
        await _db.SaveChangesAsync();

        // Accentless multi-word query must hit the accented OCR text ("działa").
        var hits = await SearchByVector("dziala kod");

        Assert.Equal([50L], hits);
    }

    [Fact]
    public async Task SearchText_TrigramSimilarity_MatchesPolishInflectionBothWays()
    {
        _db.MemeIndex.Add(IndexedMeme(60UL,
            descriptionPl: "Mem o bazie danych",
            descriptionEn: "Database meme",
            ocrText: "",
            tags: ["postgresie"]));
        _db.MemeIndex.Add(IndexedMeme(61UL,
            descriptionPl: "Inny mem o bazie danych",
            descriptionEn: "Another database meme",
            ocrText: "",
            tags: ["postgres"]));
        await _db.SaveChangesAsync();

        var forBaseForm = await SearchByTrigram("postgres");
        var forInflected = await SearchByTrigram("postgresie");

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

    private async Task<List<long>> SearchByVector(string query)
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Raw SQL: LINQ cannot express the custom public.f_unaccent function or
        // websearch_to_tsquery; this pins the migration-created FTS schema.
        command.CommandText = """
            SELECT attachment_discord_id FROM meme_index
            WHERE search_vector @@ websearch_to_tsquery('simple', public.f_unaccent($1))
            ORDER BY attachment_discord_id
            """;
        command.Parameters.AddWithValue(query);
        return await ReadIds(command);
    }

    private async Task<List<long>> SearchByTrigram(string query)
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Raw SQL: LINQ cannot express the custom public.f_unaccent / word_similarity
        // trigram functions; this pins the migration-created trigram schema.
        command.CommandText = """
            SELECT attachment_discord_id FROM meme_index
            WHERE word_similarity(public.f_unaccent($1), search_text) >= $2
            ORDER BY attachment_discord_id
            """;
        command.Parameters.AddWithValue(query);
        command.Parameters.AddWithValue(TrigramSimilarityThreshold);
        return await ReadIds(command);
    }

    private static async Task<List<long>> ReadIds(NpgsqlCommand command)
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

    private MemeIndexEntity IndexedMeme(ulong attachmentDiscordId, string descriptionPl, string descriptionEn,
        string ocrText, string[] tags)
    {
        var meme = PendingMeme(attachmentDiscordId);
        meme.Status = MemeIndexStatus.Indexed;
        meme.DescriptionPl = descriptionPl;
        meme.DescriptionEn = descriptionEn;
        meme.OcrText = ocrText;
        meme.Tags = tags;
        meme.ContentHash = $"hash-{attachmentDiscordId}";
        meme.ModelId = "google/gemini-3-flash-preview";
        meme.RawResponseJson = "{}";
        meme.IndexedAtUtc = DateTime.UtcNow;
        return meme;
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
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
