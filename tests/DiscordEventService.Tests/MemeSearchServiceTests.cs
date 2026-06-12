using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeSearchServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        _db.Guilds.Add(_guild);
        await _db.SaveChangesAsync();

        _channel = new ChannelEntity { DiscordId = ChannelDiscordId, GuildId = _guild.Id, Name = "memes", Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(_channel);
        _db.Users.Add(_author);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task SearchAsync_MultiWordAccentlessQuery_RanksMatchingRowOnly()
    {
        await SeedIndexedMemeAsync(11UL, 1001UL,
            descriptionPl: "Pies siedzi przy komputerze",
            ocrText: "kiedy kod działa za pierwszym razem",
            tags: ["pies", "programowanie"]);
        await SeedIndexedMemeAsync(12UL, 1002UL,
            descriptionPl: "Kot patrzy na lodówkę",
            ocrText: "",
            tags: ["kot"]);

        var hits = await RunSearchAsync("kod dziala");

        var hit = Assert.Single(hits);
        Assert.Equal(11UL, hit.AttachmentDiscordId);
        Assert.Equal(["pies", "programowanie"], hit.Tags);
    }

    [Fact]
    public async Task SearchAsync_TagHit_OutranksDescriptionOnlyHit()
    {
        await SeedIndexedMemeAsync(21UL, 1101UL,
            descriptionPl: "Mem o czymś zupełnie innym",
            ocrText: "",
            tags: ["rakieta"]);
        await SeedIndexedMemeAsync(22UL, 1102UL,
            descriptionPl: "Start rakieta kończy się klapą",
            ocrText: "",
            tags: ["porażka"]);

        var hits = await RunSearchAsync("rakieta");

        Assert.Equal(2, hits.Count);
        // setweight A (tags) ≫ C (descriptions) under ts_rank.
        Assert.Equal(21UL, hits[0].AttachmentDiscordId);
        Assert.Equal(22UL, hits[1].AttachmentDiscordId);
    }

    [Fact]
    public async Task SearchAsync_PolishInflectedQuery_MatchesViaTrigram()
    {
        await SeedIndexedMemeAsync(31UL, 1201UL,
            descriptionPl: "Mem o bazie danych",
            ocrText: "",
            tags: ["postgres"]);

        // FTS can't stem Polish ("postgresie" ≠ "postgres" in the simple
        // config) — word_similarity is what rescues the inflected query.
        var hits = await RunSearchAsync("postgresie");

        var hit = Assert.Single(hits);
        Assert.Equal(31UL, hit.AttachmentDiscordId);
    }

    [Fact]
    public async Task SearchAsync_RowOnSoftDeletedMessage_IsExcluded()
    {
        await SeedIndexedMemeAsync(41UL, 1301UL,
            descriptionPl: "Unikatowy żółw na deskorolce",
            ocrText: "",
            tags: ["żółw"]);
        await SeedIndexedMemeAsync(42UL, 1302UL,
            descriptionPl: "Unikatowy żółw na hulajnodze",
            ocrText: "",
            tags: ["żółw"],
            messageDeleted: true);

        var hits = await RunSearchAsync("zolw");

        var hit = Assert.Single(hits);
        Assert.Equal(41UL, hit.AttachmentDiscordId);
    }

    [Fact]
    public async Task SearchAsync_NonIndexedStatuses_AreExcluded()
    {
        // A Failed row may carry metadata from an earlier attempt; the status
        // CHECK allows it. Search must still ignore anything not Indexed.
        var failed = await SeedIndexedMemeAsync(51UL, 1401UL,
            descriptionPl: "Niepowtarzalny borsuk gra na perkusji",
            ocrText: "",
            tags: ["borsuk"]);
        failed.Status = MemeIndexStatus.Failed;
        failed.Error = "model exploded";
        failed.IndexedAtUtc = null;
        failed.ModelId = null;
        await _db.SaveChangesAsync();

        var hits = await RunSearchAsync("borsuk");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAsync_OtherGuildRows_AreExcluded()
    {
        await SeedIndexedMemeAsync(61UL, 1501UL,
            descriptionPl: "Jednorożec w innym lochu",
            ocrText: "",
            tags: ["jednorożec"],
            guildDiscordId: 999UL);

        var hits = await RunSearchAsync("jednorozec");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAsync_EqualScores_BreakTiesByMessageRecency()
    {
        // Repost dedupe copies metadata verbatim → identical scores.
        await SeedIndexedMemeAsync(71UL, 1601UL,
            descriptionPl: "Słoń maluje płot",
            ocrText: "",
            tags: ["słoń"],
            messageCreatedAtUtc: DateTime.UtcNow.AddDays(-30));
        await SeedIndexedMemeAsync(72UL, 1602UL,
            descriptionPl: "Słoń maluje płot",
            ocrText: "",
            tags: ["słoń"],
            messageCreatedAtUtc: DateTime.UtcNow.AddDays(-1));

        var hits = await RunSearchAsync("slon");

        Assert.Equal(2, hits.Count);
        Assert.Equal(72UL, hits[0].AttachmentDiscordId);
    }

    [Fact]
    public async Task SearchAsync_MoreHitsThanLimit_ReturnsOnlyLimit()
    {
        await SeedIndexedMemeAsync(81UL, 1701UL, "Trzy wielbłądy na pustyni", "", ["wielbłąd"]);
        await SeedIndexedMemeAsync(82UL, 1702UL, "Dwa wielbłądy w oazie", "", ["wielbłąd"]);
        await SeedIndexedMemeAsync(83UL, 1703UL, "Jeden wielbłąd w biurze", "", ["wielbłąd"]);

        var hits = await RunSearchAsync("wielblad", limit: 2);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        await SeedIndexedMemeAsync(91UL, 1801UL, "Pies siedzi przy komputerze", "", ["pies"]);

        var hits = await RunSearchAsync("kwantowa termodynamika frytek");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAsync_QueryWithoutWordCharacters_ReturnsEmptyWithoutQuerying()
    {
        await SeedIndexedMemeAsync(101UL, 1901UL, "Cokolwiek", "", ["cokolwiek"]);

        var hits = await RunSearchAsync("!!! ??? ((( |||");

        Assert.Empty(hits);
    }

    private async Task<List<MemeSearchHit>> RunSearchAsync(string query, int limit = MemeSearchService.DefaultLimit)
    {
        await using var db = NewContext();
        return await new MemeSearchService(db)
            .SearchAsync(GuildDiscordId, query, limit, CancellationToken.None);
    }

    private async Task<MemeIndexEntity> SeedIndexedMemeAsync(
        ulong attachmentDiscordId,
        ulong messageDiscordId,
        string descriptionPl,
        string ocrText,
        string[] tags,
        bool messageDeleted = false,
        ulong guildDiscordId = GuildDiscordId,
        DateTime? messageCreatedAtUtc = null)
    {
        var message = new MessageEntity
        {
            DiscordId = messageDiscordId,
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            CreatedAtUtc = messageCreatedAtUtc ?? DateTime.UtcNow,
            IsDeleted = messageDeleted,
            DeletedAtUtc = messageDeleted ? DateTime.UtcNow : null
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var meme = new MemeIndexEntity
        {
            MessageId = message.Id,
            GuildDiscordId = guildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = messageDiscordId,
            AttachmentDiscordId = attachmentDiscordId,
            FileName = $"meme-{attachmentDiscordId}.png",
            FileSizeBytes = 1234,
            ContentType = "image/png",
            ContentHash = $"hash-{attachmentDiscordId}",
            Status = MemeIndexStatus.Indexed,
            DescriptionPl = descriptionPl,
            DescriptionEn = "english description",
            OcrText = ocrText,
            Tags = tags,
            ModelId = "google/gemini-3-flash-preview",
            RawResponseJson = "{}",
            IndexedAtUtc = DateTime.UtcNow
        };
        _db.MemeIndex.Add(meme);
        await _db.SaveChangesAsync();
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
}
