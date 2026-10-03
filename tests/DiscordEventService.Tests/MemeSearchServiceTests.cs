using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeSearchServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;
    private const string DefaultModel = "google/gemini-3-flash-preview";
    private const ulong CutoutAttachmentId = 161UL;
    private const ulong ControlAttachmentId = 162UL;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;

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

    // The status row is the gate: an annotation can sit under a row that is not Indexed
    // (an import, or a status row that never reached Indexed). Search must not show it.
    [Theory]
    [InlineData(MemeIndexStatus.Pending)]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Skipped)]
    public async Task SearchAsync_AnnotationUnderNonIndexedStatusRow_IsNotFound(MemeIndexStatus status)
    {
        var meme = await SeedMemeAsync(51UL, 1401UL, status: status);
        await AddAnnotationAsync(meme,
            descriptionPl: "Niepowtarzalny borsuk gra na perkusji",
            ocrText: "",
            tags: ["borsuk"]);

        var hits = await RunSearchAsync("borsuk");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAsync_IndexedStatusRowWithoutAnnotation_IsNotFound()
    {
        await SeedMemeAsync(52UL, 1402UL);
        await SeedIndexedMemeAsync(53UL, 1403UL,
            descriptionPl: "Niepowtarzalny borsuk gra na perkusji",
            ocrText: "",
            tags: ["borsuk"]);

        var hits = await RunSearchAsync("borsuk");

        var hit = Assert.Single(hits);
        Assert.Equal(53UL, hit.AttachmentDiscordId);
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
        // Repost dedupe copies annotations verbatim → identical scores.
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
    public async Task SearchAsync_TwoAnnotationsOfOneMeme_ReturnsItOnceRankedByBestAnnotation()
    {
        // The weak annotation goes in first and is the newer one: a query that picks a row by
        // insert order or by indexed_at_utc instead of by score takes the wrong one.
        var meme = await SeedMemeAsync(111UL, 2001UL);
        await AddAnnotationAsync(meme,
            descriptionPl: "Start rakieta kończy się klapą",
            ocrText: "",
            tags: ["porażka"],
            modelId: "model/weak",
            indexedAtUtc: DateTime.UtcNow);
        await AddAnnotationAsync(meme,
            descriptionPl: "Mem o czymś zupełnie innym",
            ocrText: "",
            tags: ["rakieta"],
            modelId: "model/best",
            indexedAtUtc: DateTime.UtcNow.AddDays(-7));
        // Control: one annotation with exactly the best annotation's content.
        await SeedIndexedMemeAsync(112UL, 2002UL,
            descriptionPl: "Mem o czymś zupełnie innym",
            ocrText: "",
            tags: ["rakieta"],
            messageCreatedAtUtc: DateTime.UtcNow.AddDays(-1));
        await SeedIndexedMemeAsync(113UL, 2003UL,
            descriptionPl: "Statek kosmiczny na wyrzutni",
            ocrText: "rakieta",
            tags: ["kosmos"]);

        var hits = await RunSearchAsync("rakieta");

        // Tag hit (A) for 111 and its control, then the OCR hit (B). The tie goes to the newer message.
        Assert.Equal([111UL, 112UL, 113UL], hits.Select(h => h.AttachmentDiscordId));
        Assert.Equal(hits[1].Score, hits[0].Score, precision: 10);
        Assert.Equal(["rakieta"], hits[0].Tags);
        Assert.Equal("Mem o czymś zupełnie innym", hits[0].DescriptionPl);
    }

    [Fact]
    public async Task SearchAsync_QueryMatchedByOnlyOneAnnotation_StillFindsMeme()
    {
        var meme = await SeedMemeAsync(121UL, 2101UL);
        await AddAnnotationAsync(meme,
            descriptionPl: "Zwierzak śpi na kanapie",
            ocrText: "",
            tags: ["kot"],
            modelId: "model/a");
        await AddAnnotationAsync(meme,
            descriptionPl: "Skoczek narciarski w locie",
            ocrText: "",
            tags: ["małysz"],
            modelId: "model/b");

        var forSecondModel = await RunSearchAsync("malysz");
        var forFirstModel = await RunSearchAsync("kot");
        var forNeither = await RunSearchAsync("wielblad");

        Assert.Equal(121UL, Assert.Single(forSecondModel).AttachmentDiscordId);
        Assert.Equal(121UL, Assert.Single(forFirstModel).AttachmentDiscordId);
        Assert.Empty(forNeither);
    }

    [Fact]
    public async Task SearchAsync_MoreMatchingAnnotationsThanLimit_LimitCountsAttachments()
    {
        // All three annotations of 133 outscore the other two memes, so a LIMIT taken over
        // annotation rows would fill both slots with 133. The best meme has the HIGHEST attachment
        // id on purpose: a LIMIT inside the per-attachment subquery (ordered by attachment id)
        // would return 131 and 132 and lose it.
        await SeedIndexedMemeAsync(131UL, 2201UL, "Wielbłąd pije wodę w oazie", "", ["oaza"]);
        await SeedIndexedMemeAsync(132UL, 2202UL, "Wielbłąd siedzi w biurze", "", ["biuro"]);
        var meme = await SeedMemeAsync(133UL, 2203UL);
        await AddAnnotationAsync(meme, "Zwierzę na pustyni", "", ["wielbłąd"], modelId: "model/a");
        await AddAnnotationAsync(meme, "Garbate zwierzę", "", ["wielbłąd", "pustynia"], modelId: "model/b");
        await AddAnnotationAsync(meme, "Karawana o zachodzie", "", ["wielbłąd", "karawana"], modelId: "model/c");

        var hits = await RunSearchAsync("wielblad", limit: 2);

        Assert.Equal(2, hits.Count);
        Assert.Equal(2, hits.Select(h => h.AttachmentDiscordId).Distinct().Count());
        Assert.Equal(133UL, hits[0].AttachmentDiscordId);
    }

    // Weight A (#368): a hit in a field people search by outranks the same words in the OCR
    // text (B) and in a description (C).
    [Theory]
    [InlineData("templates", "paski tvp", "paski")]
    [InlineData("search_phrases", "kiedy deploy w piątek", "piatek")]
    [InlineData("people", "Adam Małysz", "malysz")]
    [InlineData("franchise", "Wiedźmin", "wiedzmin")]
    [InlineData("source", "kwejk", "kwejk")]
    public async Task SearchAsync_HitInASchemaV2Field_OutranksOcrOnlyAndDescriptionOnlyHits(string field, string value, string query)
    {
        // The field hit sits on the oldest message. Equal scores go to the newest one, so a field
        // that lost its weight cannot pass on the tie-break.
        await SeedIndexedMemeAsync(141UL, 2301UL, "Mem o czymś zupełnie innym", "", ["inne"],
            messageCreatedAtUtc: DateTime.UtcNow.AddDays(-30), configure: a => SetField(a, field, value));
        await SeedIndexedMemeAsync(142UL, 2302UL, "Napis na obrazku", ocrText: value, ["napis"],
            messageCreatedAtUtc: DateTime.UtcNow.AddDays(-20));
        await SeedIndexedMemeAsync(143UL, 2303UL, $"Obrazek: {value}", "", ["obrazek"],
            messageCreatedAtUtc: DateTime.UtcNow.AddDays(-10));

        var hits = await RunSearchAsync(query);

        Assert.Equal([141UL, 142UL, 143UL], hits.Select(h => h.AttachmentDiscordId));
        Assert.True(hits[0].Score > hits[1].Score, $"field hit {hits[0].Score} must beat the OCR hit {hits[1].Score}");
        Assert.True(hits[1].Score > hits[2].Score, $"OCR hit {hits[1].Score} must beat the description hit {hits[2].Score}");
    }

    // "other" is the bucket for a platform outside the list, not a word someone searches for.
    [Fact]
    public async Task SearchAsync_SourceOther_IsNotASearchWord()
    {
        await SeedIndexedMemeAsync(151UL, 2401UL, "Kot śpi na kanapie", "", ["kot"], configure: a => a.Source = MemeSources.Other);

        var byBucket = await RunSearchAsync("other");
        var byTag = await RunSearchAsync("kot");

        Assert.Empty(byBucket);
        Assert.Equal(151UL, Assert.Single(byTag).AttachmentDiscordId);
    }

    // Stored by the real writer, not seeded: the cut-out rule and the search columns have to meet.
    [Fact]
    public async Task SearchAsync_CutoutStoredThroughTheWriter_IsNotFoundByTheDroppedName()
    {
        await IndexCutoutAndItsControlThroughTheWriterAsync();

        var byFullName = await RunSearchAsync(FakeMemeHttpHandler.PersonName);
        var bySurname = await RunSearchAsync("kowalski");

        // Only the control: the same model output on an image kind the rule does not cover.
        Assert.Equal([ControlAttachmentId], byFullName.Select(h => h.AttachmentDiscordId));
        Assert.Equal([ControlAttachmentId], bySurname.Select(h => h.AttachmentDiscordId));
    }

    [Fact]
    public async Task SearchAsync_CutoutStoredThroughTheWriter_IsStillFoundByItsRemainingTags()
    {
        await IndexCutoutAndItsControlThroughTheWriterAsync();

        var hits = await RunSearchAsync("emotka");

        var cutout = Assert.Single(hits, h => h.AttachmentDiscordId == CutoutAttachmentId);
        Assert.Equal(FakeMemeHttpHandler.NameFreeTags, cutout.Tags);
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

    // One meme with one annotation — the shape every single-writer test needs.
    private async Task<MemeIndexEntity> SeedIndexedMemeAsync(
        ulong attachmentDiscordId,
        ulong messageDiscordId,
        string descriptionPl,
        string ocrText,
        string[] tags,
        bool messageDeleted = false,
        ulong guildDiscordId = GuildDiscordId,
        DateTime? messageCreatedAtUtc = null,
        Action<MemeAnnotationEntity>? configure = null)
    {
        var meme = await SeedMemeAsync(
            attachmentDiscordId, messageDiscordId, MemeIndexStatus.Indexed, messageDeleted, guildDiscordId, messageCreatedAtUtc);
        await AddAnnotationAsync(meme, descriptionPl, ocrText, tags, configure: configure);
        return meme;
    }

    private async Task<MemeIndexEntity> SeedMemeAsync(
        ulong attachmentDiscordId,
        ulong messageDiscordId,
        MemeIndexStatus status = MemeIndexStatus.Indexed,
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
            Status = status,
            Error = status is MemeIndexStatus.Failed or MemeIndexStatus.Skipped ? "seeded by test" : null
        };
        _db.MemeIndex.Add(meme);
        await _db.SaveChangesAsync();
        return meme;
    }

    private async Task AddAnnotationAsync(
        MemeIndexEntity meme,
        string descriptionPl,
        string ocrText,
        string[] tags,
        string modelId = DefaultModel,
        DateTime? indexedAtUtc = null,
        Action<MemeAnnotationEntity>? configure = null)
    {
        var annotation = new MemeAnnotationEntity
        {
            MemeIndexId = meme.Id,
            AttachmentDiscordId = meme.AttachmentDiscordId,
            ModelId = modelId,
            PromptVersion = OpenRouterClient.PromptVersion,
            IndexedAtUtc = indexedAtUtc ?? DateTime.UtcNow,
            DescriptionPl = descriptionPl,
            DescriptionEn = "english description",
            OcrText = ocrText,
            Tags = tags,
            RawResponseJson = "{}"
        };
        configure?.Invoke(annotation);
        _db.MemeAnnotations.Add(annotation);
        await _db.SaveChangesAsync();
    }

    private static void SetField(MemeAnnotationEntity annotation, string field, string value)
    {
        switch (field)
        {
            case "templates":
                annotation.Templates = [value];
                break;
            case "search_phrases":
                annotation.SearchPhrases = [value];
                break;
            // Both columns, as the writer fills them. Only people_names feeds the search columns.
            case "people":
                annotation.People = JsonSerializer.Serialize(
                    new[] { new MemePerson { Name = value, Evidence = MemePersonEvidence.WidelyRecognized } });
                annotation.PeopleNames = [value];
                break;
            case "franchise":
                annotation.Franchise = value;
                break;
            case "source":
                annotation.Source = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "not a schema v2 weight-A field");
        }
    }

    // One message with two images, through the live hook and MemeAttachmentIndexer: a cut-out
    // that names a person, and the same model output on another image kind.
    private async Task IndexCutoutAndItsControlThroughTheWriterAsync()
    {
        const ulong MessageDiscordId = 2501UL;
        byte[] cutoutImage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 1, 1];
        byte[] controlImage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 2, 2, 2];
        var http = new FakeMemeHttpHandler();
        http.SetImage(CutoutAttachmentId, cutoutImage);
        http.SetImage(ControlAttachmentId, controlImage);
        http.CutoutFor.Add(cutoutImage);
        http.NamedPersonFor.Add(controlImage);

        // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
        var attachments = new[] { CutoutAttachmentId, ControlAttachmentId }.Select(id =>
            $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{ChannelDiscordId}/{id}/meme-{id}.png?ex=expired\",\"FileName\":\"meme-{id}.png\",\"FileSize\":123}}");
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = MessageDiscordId,
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = [ChannelDiscordId];
            // The live hook does nothing without it (#369).
            o.AutomaticIndexing = true;
        });
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = DefaultModel;
            o.RequestDelayMs = 0;
        });
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(http));
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeAttachmentIndexer>();
        services.AddScoped<BackfillJobExecutor>();
        services.AddScoped<MemeIndexingJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .IndexMessageAsync(GuildDiscordId, MessageDiscordId, CancellationToken.None);
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
