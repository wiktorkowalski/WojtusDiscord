using DiscordEventService.Configuration;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Dtos;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// #395: the queries behind the dashboard's "Meme index" page, each against a real database.
public sealed class MemeStatsReaderTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // AnswerWaitTimeout of a test that holds a gate.
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(200);

    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private DiscordDbContext _db = null!;
    private MemeStatsTestData _data = null!;
    private MemeSearchLogWriter _searchLog = null!;
    private MemeDashboardLimits _limits = null!;

    public async Task InitializeAsync()
    {
        _limits = new MemeDashboardLimits();
        _db = NewContext();
        await _db.Database.MigrateAsync();
        _data = new MemeStatsTestData(_db);
        await _data.ResetAsync();
        _searchLog = MemeSearchTestServices.NewLogWriter(fixture.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _limits.Dispose();
        await _db.DisposeAsync();
    }

    // ───────────────────────────── Index state ─────────────────────────────

    [Fact]
    public async Task GetIndexAsync_EmptyDatabase_ReturnsZerosAndEmptyLists()
    {
        var index = await IndexAsync();

        Assert.Equal(new MemeStatusCountsDto(0, 0, 0, 0, 0), index.Status);
        Assert.Equal(0, index.RefusalCount);
        Assert.Equal(0, index.TotalFileSizeBytes);
        Assert.Equal(0, index.SearchableCount);
        Assert.Empty(index.Writers);
        Assert.Null(index.LastAnnotationAtUtc);
        Assert.Equal(new MemeNotIndexedDto(0, null), index.NotIndexed);
        Assert.Empty(index.ByYear);
        Assert.All(
            new[]
            {
                index.Distributions.ImageKind, index.Distributions.Language, index.Distributions.Source,
                index.Distributions.Templates, index.Distributions.Franchises, index.Distributions.Tags,
            },
            d =>
            {
                Assert.Empty(d.Buckets);
                Assert.Equal(0, d.DistinctCount);
                Assert.Equal(0, d.MissingCount);
            });
    }

    [Fact]
    public async Task GetIndexAsync_RowsOfEveryStatus_CountsStatusRefusalsAndFileSize()
    {
        await _data.AddIndexedAsync(1UL);
        await _data.AddMemeAsync(2UL, fileSizeBytes: 1000);
        await _data.AddMemeAsync(3UL, MemeIndexStatus.Pending, fileSizeBytes: 20);
        await _data.AddMemeAsync(4UL, MemeIndexStatus.Failed, fileSizeBytes: 3);
        await _data.AddMemeAsync(5UL, MemeIndexStatus.Skipped, fileSizeBytes: 4, refused: true);
        // A refusal is the writer's outcome, not the row's: an Indexed row can carry one.
        await _data.AddMemeAsync(6UL, fileSizeBytes: 5, refused: true);

        var index = await IndexAsync();

        Assert.Equal(new MemeStatusCountsDto(Pending: 1, Indexed: 3, Failed: 1, Skipped: 1, Total: 6), index.Status);
        Assert.Equal(2, index.RefusalCount);
        Assert.Equal(100 + 1000 + 20 + 3 + 4 + 5, index.TotalFileSizeBytes);
    }

    // The endpoint has no auth and the page asks on every load: inside the window nothing is computed again.
    [Fact]
    public async Task GetIndexAsync_SecondCallInsideTheCacheWindow_ReturnsTheKeptAnswer()
    {
        await _data.AddIndexedAsync(1UL);
        var first = await IndexAsync();
        await _data.AddIndexedAsync(2UL);

        var second = await IndexAsync();

        Assert.Same(first, second);
        Assert.Equal(1, second.Status.Indexed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetIndexAsync_AutomaticIndexingOption_IsReportedWithTheMemeChannels(bool automaticIndexing)
    {
        var index = await IndexAsync(automaticIndexing);

        Assert.Equal(automaticIndexing, index.AutomaticIndexing);
        Assert.Equal([new MemeChannelDto(MemeStatsTestData.ChannelDiscordId, MemeStatsTestData.ChannelName)], index.Channels);
    }

    [Fact]
    public async Task GetIndexAsync_TwoWriters_ReportsEachWithItsCoverageOfTheIndexedMemes()
    {
        var first = await _data.AddMemeAsync(1UL);
        await _data.AddAnnotationAsync(first, MemeStatsTestData.ModelA, Noon);
        await _data.AddAnnotationAsync(first, MemeStatsTestData.ModelB, Noon.AddDays(2));
        foreach (var attachmentId in new[] { 2UL, 3UL, 4UL })
            await _data.AddAnnotationAsync(await _data.AddMemeAsync(attachmentId), MemeStatsTestData.ModelA, Noon.AddDays(1));

        var index = await IndexAsync();

        Assert.Equal(
            [
                new MemeWriterDto(MemeStatsTestData.ModelA, OpenRouterClient.PromptVersion, 4, 100.0, Noon.AddDays(1)),
                new MemeWriterDto(MemeStatsTestData.ModelB, OpenRouterClient.PromptVersion, 1, 25.0, Noon.AddDays(2)),
            ],
            index.Writers);
        Assert.Equal(Noon.AddDays(2), index.LastAnnotationAtUtc);
    }

    // ───────────────────────────── Not indexed yet ─────────────────────────────

    [Fact]
    public async Task GetIndexAsync_ImagesWithNoRow_ReportsTheirCountAndTheOldestPostDate()
    {
        await _data.AddIndexedAsync(1UL, postedAtUtc: Noon.AddYears(-3));
        await _data.AddImageMessageAsync(2UL, Noon.AddDays(-5));
        await _data.AddImageMessageAsync(3UL, Noon.AddDays(-9));
        await _data.AddImageMessageAsync(4UL, Noon.AddDays(-20), messageDeleted: true);

        var index = await IndexAsync();

        Assert.Equal(new MemeNotIndexedDto(2, Noon.AddDays(-9)), index.NotIndexed);
    }

    // The Overview tile and this page read one definition (#397).
    [Fact]
    public async Task GetIndexAsync_ImagesWithNoRow_AgreesWithTheOverviewWaitingCount()
    {
        await _data.AddIndexedAsync(1UL);
        await _data.AddImageMessageAsync(2UL);
        await _data.AddImageMessageAsync(3UL);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var index = await IndexAsync();
        var overview = (await new StatsController(_db).Overview(NewSummaryReader(cache), default)).Value!;

        Assert.Equal(2, index.NotIndexed.Count);
        Assert.Equal(overview.MemeWaitingCount, index.NotIndexed.Count);
        Assert.Equal(overview.MemeIndexedCount, index.Status.Indexed);
    }

    // ───────────────────────────── By year ─────────────────────────────

    [Fact]
    public async Task GetIndexAsync_MemesAcrossYears_BucketsByWarsawYearAndFillsTheGaps()
    {
        await _data.AddIndexedAsync(1UL, postedAtUtc: new DateTime(2021, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        // 23:30 UTC on 31 December is already 00:30 on 1 January in Warsaw.
        await _data.AddIndexedAsync(2UL, postedAtUtc: new DateTime(2023, 12, 31, 23, 30, 0, DateTimeKind.Utc));
        await _data.AddIndexedAsync(3UL, postedAtUtc: new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        var index = await IndexAsync();

        Assert.Equal(
            [new MemeYearCountDto(2021, 1), new MemeYearCountDto(2022, 0), new MemeYearCountDto(2023, 0), new MemeYearCountDto(2024, 2)],
            index.ByYear);
        Assert.Equal(3, index.SearchableCount);
    }

    // Search does not return these two either.
    [Fact]
    public async Task GetIndexAsync_DeletedMessageOrRowNotIndexed_IsInNoYearAndNoDistribution()
    {
        var year = new DateTime(2022, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["kept"], year);
        var deleted = await _data.AddMemeAsync(2UL, postedAtUtc: year.AddYears(-1), messageDeleted: true);
        await _data.AddAnnotationAsync(deleted, configure: a => a.Tags = ["deleted"]);
        var pending = await _data.AddMemeAsync(3UL, MemeIndexStatus.Pending, postedAtUtc: year.AddYears(1));
        await _data.AddAnnotationAsync(pending, configure: a => a.Tags = ["pending"]);

        var index = await IndexAsync();

        Assert.Equal([new MemeYearCountDto(2022, 1)], index.ByYear);
        Assert.Equal(1, index.SearchableCount);
        Assert.Equal(2, index.Status.Indexed);
        Assert.Equal([new MemeBucketDto("kept", 1)], index.Distributions.Tags.Buckets);
    }

    // ───────────────────────────── Distributions ─────────────────────────────

    // A second writer must not double the numbers: the latest annotation is the one read.
    [Fact]
    public async Task GetIndexAsync_MemeWithTwoAnnotations_CountsOnceWithTheLatestAnnotation()
    {
        var meme = await _data.AddMemeAsync(1UL);
        await _data.AddAnnotationAsync(meme, MemeStatsTestData.ModelB, Noon, a =>
        {
            a.ImageKind = MemeImageKind.Comic;
            a.Language = MemeLanguage.En;
            a.Source = "reddit";
            a.Franchise = "Old";
            a.Templates = ["old template"];
            a.Tags = ["old"];
        });
        await _data.AddAnnotationAsync(meme, MemeStatsTestData.ModelA, Noon.AddHours(1), a =>
        {
            a.ImageKind = MemeImageKind.TemplateMeme;
            a.Language = MemeLanguage.Pl;
            a.Source = "jbzd";
            a.Franchise = "New";
            a.Templates = ["new template"];
            a.Tags = ["new"];
        });

        var distributions = (await IndexAsync()).Distributions;

        Assert.Equal([new MemeBucketDto("template_meme", 1)], distributions.ImageKind.Buckets);
        Assert.Equal([new MemeBucketDto("pl", 1)], distributions.Language.Buckets);
        Assert.Equal([new MemeBucketDto("jbzd", 1)], distributions.Source.Buckets);
        Assert.Equal([new MemeBucketDto("New", 1)], distributions.Franchises.Buckets);
        Assert.Equal([new MemeBucketDto("new template", 1)], distributions.Templates.Buckets);
        Assert.Equal([new MemeBucketDto("new", 1)], distributions.Tags.Buckets);
    }

    // The tiebreak of search: the same indexed_at_utc, then the lower model id.
    [Fact]
    public async Task GetIndexAsync_TwoAnnotationsWrittenAtTheSameTime_ReadsTheLowerModelId()
    {
        var meme = await _data.AddMemeAsync(1UL);
        await _data.AddAnnotationAsync(meme, MemeStatsTestData.ModelB, Noon, a => a.Tags = ["from b"]);
        await _data.AddAnnotationAsync(meme, MemeStatsTestData.ModelA, Noon, a => a.Tags = ["from a"]);

        var tags = (await IndexAsync()).Distributions.Tags;

        Assert.Equal([new MemeBucketDto("from a", 1)], tags.Buckets);
    }

    [Fact]
    public async Task GetIndexAsync_ImageKindNull_IsItsOwnBucketAndNotOther()
    {
        await _data.AddIndexedAsync(1UL, a => a.ImageKind = MemeImageKind.Other);
        await _data.AddIndexedAsync(2UL, a => a.ImageKind = MemeImageKind.Other);
        await _data.AddIndexedAsync(3UL, a => a.ImageKind = MemeImageKind.CutoutFaceOrEmote);
        foreach (var attachmentId in new[] { 4UL, 5UL, 6UL })
            await _data.AddIndexedAsync(attachmentId, a => a.ImageKind = null);

        var imageKind = (await IndexAsync()).Distributions.ImageKind;

        // The null bucket is last, also when it is the largest.
        Assert.Equal(
            [new MemeBucketDto("other", 2), new MemeBucketDto("cutout_face_or_emote", 1), new MemeBucketDto(null, 3)],
            imageKind.Buckets);
        Assert.Equal(2, imageKind.DistinctCount);
        Assert.Equal(3, imageKind.MissingCount);
    }

    [Fact]
    public async Task GetIndexAsync_LanguageNull_IsItsOwnBucketAndNotNone()
    {
        await _data.AddIndexedAsync(1UL, a => a.Language = MemeLanguage.None);
        await _data.AddIndexedAsync(2UL, a => a.Language = MemeLanguage.Mixed);
        await _data.AddIndexedAsync(3UL, a => a.Language = null);

        var language = (await IndexAsync()).Distributions.Language;

        // A tie on the count: by name.
        Assert.Equal(
            [new MemeBucketDto("mixed", 1), new MemeBucketDto("none", 1), new MemeBucketDto(null, 1)],
            language.Buckets);
        Assert.Equal(2, language.DistinctCount);
        Assert.Equal(1, language.MissingCount);
    }

    [Fact]
    public async Task GetIndexAsync_SourceNull_IsItsOwnBucketAndNotOther()
    {
        await _data.AddIndexedAsync(1UL, a => a.Source = MemeSources.Other);
        await _data.AddIndexedAsync(2UL, a => a.Source = "jbzd");
        await _data.AddIndexedAsync(3UL, a => a.Source = "jbzd");
        await _data.AddIndexedAsync(4UL, a => a.Source = null);
        await _data.AddIndexedAsync(5UL, a => a.Source = null);

        var source = (await IndexAsync()).Distributions.Source;

        Assert.Equal(
            [new MemeBucketDto("jbzd", 2), new MemeBucketDto("other", 1), new MemeBucketDto(null, 2)],
            source.Buckets);
        Assert.Equal(2, source.DistinctCount);
        Assert.Equal(2, source.MissingCount);
    }

    [Fact]
    public async Task GetIndexAsync_MoreTagsThanTheTopList_CutsByCountThenNameAndReportsAllDistinct()
    {
        // "zebra" on two memes; ten more tags on one meme each; one meme with no tag.
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["zebra", "k", "j", "i", "h", "g"]);
        await _data.AddIndexedAsync(2UL, a => a.Tags = ["zebra", "f", "e", "d", "c", "b"]);
        // The same tag twice on one meme is one meme.
        await _data.AddIndexedAsync(3UL, a => a.Tags = ["zebra", "zebra"]);
        await _data.AddIndexedAsync(4UL, a => a.Tags = []);

        var tags = (await IndexAsync()).Distributions.Tags;

        Assert.Equal(MemeStatsReader.TopListSize, tags.Buckets.Count);
        Assert.Equal(new MemeBucketDto("zebra", 3), tags.Buckets[0]);
        Assert.Equal(["b", "c", "d", "e", "f", "g", "h"], tags.Buckets.Skip(1).Select(b => b.Name));
        Assert.All(tags.Buckets.Skip(1), b => Assert.Equal(1, b.Count));
        Assert.Equal(11, tags.DistinctCount);
        Assert.Equal(1, tags.MissingCount);
    }

    [Fact]
    public async Task GetIndexAsync_Templates_CountsMemesPerTemplateAndTheMemesWithNone()
    {
        await _data.AddIndexedAsync(1UL, a => a.Templates = ["wojak", "doge"]);
        await _data.AddIndexedAsync(2UL, a => a.Templates = ["wojak"]);
        await _data.AddIndexedAsync(3UL, a => a.Templates = []);
        await _data.AddIndexedAsync(4UL, a => a.Templates = []);

        var templates = (await IndexAsync()).Distributions.Templates;

        Assert.Equal([new MemeBucketDto("wojak", 2), new MemeBucketDto("doge", 1)], templates.Buckets);
        Assert.Equal(2, templates.DistinctCount);
        Assert.Equal(2, templates.MissingCount);
    }

    [Fact]
    public async Task GetIndexAsync_MoreFranchisesThanTheTopList_CutsByCountThenNameWithNoNullBucket()
    {
        ulong attachmentId = 1UL;
        // Ten franchises on one meme each, "Wiedźmin" on two, two memes with none.
        foreach (var franchise in new[] { "J", "I", "H", "G", "F", "E", "D", "C", "B", "A", "Wiedźmin", "Wiedźmin" })
            await _data.AddIndexedAsync(attachmentId++, a => a.Franchise = franchise);
        await _data.AddIndexedAsync(attachmentId++, a => a.Franchise = null);
        await _data.AddIndexedAsync(attachmentId, a => a.Franchise = null);

        var franchises = (await IndexAsync()).Distributions.Franchises;

        Assert.Equal(["Wiedźmin", "A", "B", "C", "D", "E", "F", "G"], franchises.Buckets.Select(b => b.Name));
        Assert.Equal(2, franchises.Buckets[0].Count);
        Assert.Equal(11, franchises.DistinctCount);
        Assert.Equal(2, franchises.MissingCount);
    }

    // ───────────────────────────── Search usage ─────────────────────────────

    [Fact]
    public async Task GetSearchUsageAsync_EmptyLog_ReturnsZerosAndNoRates()
    {
        var usage = await UsageAsync(30);

        Assert.Equal(30, usage.Days);
        Assert.Equal(0, usage.SearchCount);
        Assert.Equal(0, usage.ZeroResultCount);
        Assert.Null(usage.ZeroResultRate);
        Assert.Null(usage.DurationP95Ms);
        Assert.Equal(new MemeSearchSourceCountsDto(0, 0, 0, 0), usage.BySource);
        Assert.Empty(usage.Latest);
    }

    [Fact]
    public async Task GetSearchUsageAsync_MixedLog_CountsStartedSearchesBySourceWithRateAndP95()
    {
        await _data.AddIndexedAsync(1UL);
        var now = DateTime.UtcNow;
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, now.AddHours(-1), "a", durationMs: 10, hits: 1UL);
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, now.AddHours(-2), "b", durationMs: 20, hits: 1UL);
        await _data.AddSearchAsync(MemeSearchSource.AssistantTool, now.AddHours(-3), "c", durationMs: 30);
        await _data.AddSearchAsync(MemeSearchSource.Other, now.AddHours(-4), "d", durationMs: 40, hits: 1UL);
        // In none of the search numbers: a page turn, a source this page does not know, a row before the window.
        await _data.AddSearchAsync(MemeSearchSource.PageButton, now.AddHours(-5), "a", durationMs: 1000);
        await _data.AddSearchAsync((MemeSearchSource)99, now.AddHours(-6), "later source", durationMs: 5000);
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, now.AddDays(-8), "old", durationMs: 9000);

        var usage = await UsageAsync(7);

        Assert.Equal(4, usage.SearchCount);
        Assert.Equal(1, usage.ZeroResultCount);
        Assert.Equal(0.25, usage.ZeroResultRate);
        // percentile_cont over 10, 20, 30, 40: 30 + 0.85 * 10.
        Assert.Equal(38.5, usage.DurationP95Ms!.Value, precision: 6);
        Assert.Equal(new MemeSearchSourceCountsDto(SlashCommand: 2, AssistantTool: 1, PageButton: 1, Other: 1), usage.BySource);
        Assert.Equal(["a", "b", "c", "d"], usage.Latest.Select(s => s.Query));
        Assert.Equal(["slashCommand", "slashCommand", "assistantTool", "other"], usage.Latest.Select(s => s.Source));
    }

    [Fact]
    public async Task GetSearchUsageAsync_LatestSearch_CarriesItsTopHitWithLinksAndNoPerson()
    {
        var meme = await _data.AddIndexedAsync(1UL);
        var searchedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await _data.AddSearchAsync(
            MemeSearchSource.SlashCommand, searchedAtUtc, "rakieta", durationMs: 12.5,
            userDiscordId: 424242UL, channelDiscordId: 777777UL, hits: [1UL, 99UL]);

        var search = Assert.Single((await UsageAsync(30)).Latest);

        Assert.Equal(searchedAtUtc, search.SearchedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal("rakieta", search.Query);
        Assert.Equal(2, search.ResultCount);
        Assert.Equal(12.5, search.DurationMs);
        Assert.Equal(
            new MemeLoggedTopHitDto(
                1UL,
                1.0,
                "meme-1.png",
                $"opis 1 {MemeStatsTestData.ModelA}",
                $"https://discord.com/channels/{MemeStatsTestData.GuildDiscordId}/{MemeStatsTestData.ChannelDiscordId}/{meme.MessageDiscordId}",
                "/api/stats/memes/thumbnails/1"),
            search.TopHit);
    }

    [Fact]
    public async Task GetSearchUsageAsync_TopHitThatLeftTheIndex_KeepsItsIdAndScoreOnly()
    {
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-5), "gone", durationMs: 5, hits: 99UL);

        var search = Assert.Single((await UsageAsync(30)).Latest);

        Assert.Equal(new MemeLoggedTopHitDto(99UL, 1.0, null, null, null, null), search.TopHit);
    }

    // Search no longer returns these memes, so the usage list must not show them either:
    // no file name, no description, no link, no thumbnail.
    [Theory]
    [InlineData(true, MemeIndexStatus.Indexed)]
    [InlineData(false, MemeIndexStatus.Skipped)]
    [InlineData(false, MemeIndexStatus.Pending)]
    public async Task GetSearchUsageAsync_TopHitThatSearchNoLongerReturns_KeepsItsIdAndScoreOnly(
        bool messageDeleted, MemeIndexStatus status)
    {
        var meme = await _data.AddMemeAsync(1UL, status, messageDeleted: messageDeleted);
        await _data.AddAnnotationAsync(meme);
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-5), "rakieta", durationMs: 5, hits: 1UL);

        var search = Assert.Single((await UsageAsync(30)).Latest);

        Assert.Equal(new MemeLoggedTopHitDto(1UL, 1.0, null, null, null, null), search.TopHit);
    }

    // The endpoint has no auth: inside the window an answer for the same days is not computed again.
    [Fact]
    public async Task GetSearchUsageAsync_SecondCallWithTheSameDays_ReturnsTheKeptAnswer()
    {
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-5), "a", durationMs: 5);
        var first = await UsageAsync(30);
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-4), "b", durationMs: 5);

        var second = await UsageAsync(30);
        var otherDays = await UsageAsync(90);

        Assert.Same(first, second);
        Assert.Equal(1, second.SearchCount);
        // Each value of days has its own answer.
        Assert.Equal(2, otherDays.SearchCount);
    }

    // Many requests at once for one value of days compute one answer.
    [Fact]
    public async Task GetSearchUsageAsync_ConcurrentCallsWithTheSameDays_ComputeOnce()
    {
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-5), "a", durationMs: 5);
        var contexts = Enumerable.Range(0, 6).Select(_ => NewContext()).ToList();
        try
        {
            var answers = await Task.WhenAll(contexts.Select(db =>
                Task.Run(() => NewReader(db: db).GetSearchUsageAsync(30, CancellationToken.None))));

            Assert.All(answers, a => Assert.Same(answers[0], a));
        }
        finally
        {
            foreach (var db in contexts)
                await db.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetSearchUsageAsync_MoreSearchesThanTheList_ReturnsTheNewestOnes()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < MemeStatsReader.LatestSearchCount + 3; i++)
            await _data.AddSearchAsync(MemeSearchSource.SlashCommand, now.AddMinutes(-i - 1), $"q{i}", durationMs: 1);

        var usage = await UsageAsync(30);

        Assert.Equal(MemeStatsReader.LatestSearchCount + 3, usage.SearchCount);
        Assert.Equal(
            Enumerable.Range(0, MemeStatsReader.LatestSearchCount).Select(i => $"q{i}"),
            usage.Latest.Select(s => s.Query));
    }

    // ───────────────────────────── Tester search ─────────────────────────────

    [Fact]
    public async Task SearchAsync_MatchingMemes_ReturnsRankedHitsWithTheScoreParts()
    {
        var tagHit = await _data.AddIndexedAsync(1UL, a =>
        {
            a.DescriptionPl = "Mem o czymś zupełnie innym";
            a.Tags = ["rakieta", "kosmos"];
            a.Templates = ["drake"];
            a.ImageKind = MemeImageKind.TemplateMeme;
        });
        await _data.AddIndexedAsync(2UL, a => a.DescriptionPl = "Start rakieta kończy się klapą");
        await _data.AddIndexedAsync(3UL, a => a.DescriptionPl = "Kot patrzy na lodówkę");

        var result = await SearchAsync("rakieta", limit: 1, CancellationToken.None);

        Assert.Equal("rakieta", result.Query);
        Assert.Equal(2, result.Total);
        Assert.Equal(0.5, result.TrigramWeight);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(1, hit.Rank);
        Assert.Equal(1UL, hit.AttachmentDiscordId);
        Assert.Equal(tagHit.MessageDiscordId, hit.MessageDiscordId);
        Assert.Equal(MemeStatsTestData.ChannelDiscordId, hit.ChannelDiscordId);
        Assert.Equal("meme-1.png", hit.FileName);
        Assert.Equal("Mem o czymś zupełnie innym", hit.DescriptionPl);
        Assert.Equal("template_meme", hit.ImageKind);
        Assert.Equal(["drake"], hit.Templates);
        Assert.Equal(["rakieta", "kosmos"], hit.Tags);
        Assert.Equal(MemeStatsTestData.ModelA, hit.ModelId);
        Assert.Equal(OpenRouterClient.PromptVersion, hit.PromptVersion);
        Assert.True(hit.TsRank > 0, $"ts_rank {hit.TsRank}");
        Assert.True(hit.TrigramSimilarity > 0, $"trigram {hit.TrigramSimilarity}");
        // The two parts are the score: the page prints this sum.
        Assert.Equal(hit.TsRank + result.TrigramWeight * hit.TrigramSimilarity, hit.Score, precision: 6);
        Assert.Equal(
            $"https://discord.com/channels/{MemeStatsTestData.GuildDiscordId}/{MemeStatsTestData.ChannelDiscordId}/{tagHit.MessageDiscordId}",
            hit.JumpUrl);
        Assert.Equal("/api/stats/memes/thumbnails/1", hit.ThumbnailUrl);
    }

    [Fact]
    public async Task SearchAsync_AnnotationFromBeforeSchemaV2_HasNoImageKind()
    {
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["rakieta"]);

        var hit = Assert.Single((await SearchAsync("rakieta", 5, CancellationToken.None)).Hits);

        Assert.Null(hit.ImageKind);
        Assert.Empty(hit.Templates);
    }

    // The API is read-only and has no auth: a tester search must not be a way to insert rows.
    [Theory]
    [InlineData("rakieta")]
    [InlineData("kwantowa termodynamika frytek")]
    [InlineData("!!! ???")]
    public async Task SearchAsync_TesterSearch_WritesNothingToTheSearchLog(string query)
    {
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["rakieta"]);

        await SearchAsync(query, 5, CancellationToken.None);
        await _searchLog.LastWrite;

        Assert.Equal(0, await _db.MemeSearchLog.CountAsync());
        Assert.Equal(0, await _db.MemeSearchLogResults.CountAsync());
        Assert.Equal(0, (await UsageAsync(30)).SearchCount);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsNoHits()
    {
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["rakieta"]);

        var result = await SearchAsync("kwantowa termodynamika frytek", 5, CancellationToken.None);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task SearchAsync_NothingIndexed_ReturnsNoHits()
    {
        await _data.AddMemeAsync(1UL, MemeIndexStatus.Pending);

        var result = await SearchAsync("rakieta", 5, CancellationToken.None);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.Total);
    }

    // The endpoint has no auth and the search scans the corpus: past the cap a search is not run.
    [Fact]
    public async Task SearchAsync_EverySearchSlotTaken_ReturnsNullUntilASlotIsFree()
    {
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["rakieta"]);
        for (var i = 0; i < MemeDashboardLimits.MaxConcurrentSearches; i++)
            await _limits.SearchGate.WaitAsync();

        var busy = await NewReader().SearchAsync("rakieta", 5, CancellationToken.None);
        _limits.SearchGate.Release(MemeDashboardLimits.MaxConcurrentSearches);
        var free = await NewReader().SearchAsync("rakieta", 5, CancellationToken.None);

        Assert.Null(busy);
        Assert.Single(free!.Hits);
    }

    // #408: the slots cap how many searches run at one time, not how many a minute.
    [Fact]
    public async Task SearchAsync_OverTheSearchesOfTheMinute_ReturnsNullAndRunsNoSearch()
    {
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["rakieta"]);
        using var limits = new MemeDashboardLimits { SearchesPerMinute = 2 };

        var first = await NewReader(limits: limits).SearchAsync("rakieta", 5, CancellationToken.None);
        var second = await NewReader(limits: limits).SearchAsync("rakieta", 5, CancellationToken.None);
        // A disposed context: a search that ran would throw, so null means nothing touched the database.
        var disposed = NewContext();
        await disposed.DisposeAsync();
        var overTheLimit = await NewReader(db: disposed, limits: limits).SearchAsync("rakieta", 5, CancellationToken.None);

        Assert.Single(first!.Hits);
        Assert.Single(second!.Hits);
        Assert.Null(overTheLimit);
        // The slot it held for the check is free again.
        Assert.Equal(MemeDashboardLimits.MaxConcurrentSearches, limits.SearchGate.CurrentCount);
    }

    // The budget is one for every caller: requests the gate turns away must not use it up.
    [Fact]
    public async Task SearchAsync_TurnedAwayByTheGate_SpendsNoPermitOfTheMinute()
    {
        await _data.AddIndexedAsync(1UL, a => a.Tags = ["rakieta"]);
        using var limits = new MemeDashboardLimits { SearchesPerMinute = 1 };
        for (var i = 0; i < MemeDashboardLimits.MaxConcurrentSearches; i++)
            await limits.SearchGate.WaitAsync();

        var busy = await NewReader(limits: limits).SearchAsync("rakieta", 5, CancellationToken.None);
        limits.SearchGate.Release(MemeDashboardLimits.MaxConcurrentSearches);
        var free = await NewReader(limits: limits).SearchAsync("rakieta", 5, CancellationToken.None);

        Assert.Null(busy);
        Assert.Single(free!.Hits);
    }

    // The wait for an answer that another request computes has an end: then "busy", not a longer queue.
    [Fact]
    public async Task GetIndexAsync_AnotherRequestHoldsTheGatePastTheWaitLimit_ReturnsNullAndComputesNothing()
    {
        await _data.AddIndexedAsync(1UL);
        using var limits = new MemeDashboardLimits { AnswerWaitTimeout = ShortWait };
        await limits.IndexGate.WaitAsync();

        var busy = await NewReader(limits: limits).GetIndexAsync(CancellationToken.None);
        limits.IndexGate.Release();
        var free = await NewReader(limits: limits).GetIndexAsync(CancellationToken.None);

        Assert.Null(busy);
        Assert.Equal(1, free!.Status.Indexed);
    }

    [Fact]
    public async Task GetSearchUsageAsync_AnotherRequestHoldsTheGatePastTheWaitLimit_ReturnsNullAndComputesNothing()
    {
        await _data.AddSearchAsync(MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-5), "a", durationMs: 5);
        using var limits = new MemeDashboardLimits { AnswerWaitTimeout = ShortWait };
        await limits.SearchUsageGate.WaitAsync();

        var busy = await NewReader(limits: limits).GetSearchUsageAsync(30, CancellationToken.None);
        var keptWhileBusy = limits.SearchUsage.Count;
        limits.SearchUsageGate.Release();
        var free = await NewReader(limits: limits).GetSearchUsageAsync(30, CancellationToken.None);

        Assert.Null(busy);
        Assert.Equal(0, keptWhileBusy);
        Assert.Equal(1, free!.SearchCount);
    }

    // A kept answer needs no gate: a request for it is served while another value is computed.
    [Fact]
    public async Task GetSearchUsageAsync_KeptAnswerWhileTheGateIsHeld_IsServedAtOnce()
    {
        var kept = await UsageAsync(30);
        await _limits.SearchUsageGate.WaitAsync();

        var served = await NewReader().GetSearchUsageAsync(30, CancellationToken.None);
        _limits.SearchUsageGate.Release();

        Assert.Same(kept, served);
    }

    private async Task<MemeIndexDto> IndexAsync(bool automaticIndexing = false) =>
        (await NewReader(automaticIndexing).GetIndexAsync(CancellationToken.None))!;

    private async Task<MemeSearchUsageDto> UsageAsync(int days) =>
        (await NewReader().GetSearchUsageAsync(days, CancellationToken.None))!;

    private async Task<MemeSearchResultDto> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
        (await NewReader().SearchAsync(query, limit, cancellationToken))!;

    // db: a context of its own for a reader that runs next to others, as every request has.
    // limits: its own caps for a test that holds a gate and must not wait the real time.
    private MemeStatsReader NewReader(
        bool automaticIndexing = false, DiscordDbContext? db = null, MemeDashboardLimits? limits = null)
    {
        var context = db ?? _db;
        var options = NewOptions(automaticIndexing);
        // A cache of its own: every reader computes the waiting count from the database.
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new MemeStatsReader(
            context, NewSummaryReader(cache, context), new MemeSearchService(context, _searchLog), limits ?? _limits, options);
    }

    private MemeIndexSummaryReader NewSummaryReader(IMemoryCache cache, DiscordDbContext? db = null) =>
        new(db ?? _db, new MemeSampleService(db ?? _db, NewOptions(), NullLogger<MemeSampleService>.Instance), cache);

    private static IOptions<MemeIndexOptions> NewOptions(bool automaticIndexing = false) =>
        Options.Create(new MemeIndexOptions
        {
            ChannelIds = [MemeStatsTestData.ChannelDiscordId],
            AutomaticIndexing = automaticIndexing,
        });

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
