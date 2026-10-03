using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeIndexingJobTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;
    private const string ConfiguredModel = "test/model";
    // Mirrors the MemeIndexOptions.MaxImageBytes default.
    private const int DefaultMaxImageBytes = 25 * 1024 * 1024;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;
    private FakeMemeHttpHandler _http = null!;
    private readonly RecordingLogger _indexerLog = new();

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeAnnotations.ExecuteDeleteAsync();
        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.BackfillCheckpoints.ExecuteDeleteAsync();
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

        _http = new FakeMemeHttpHandler();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ExecuteAsync_FullRun_IndexesEveryImageAttachment()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        AddMessage(1002UL, Attachment(13UL, "c.png"), Attachment(14UL, "clip.mp4"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));

        await RunJobAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 12UL, 13UL], rows.Select(r => r.AttachmentDiscordId));
        Assert.All(rows, r =>
        {
            Assert.Equal(MemeIndexStatus.Indexed, r.Status);
            Assert.Equal(GuildDiscordId, r.GuildDiscordId);
            Assert.Equal(ChannelDiscordId, r.ChannelDiscordId);
            Assert.NotNull(r.ContentHash);
            Assert.NotEqual(Guid.Empty, r.MessageId);
        });
        Assert.Equal(3, _http.ModelCalls);

        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
        Assert.Equal(3, checkpoint.ProcessedCount);
    }

    // The effort is provenance on the annotation: unset and "" both mean the model's own default.
    [Theory]
    [InlineData("low", "low")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public async Task ExecuteAsync_FullRun_WritesOneAnnotationPerAttachment(string? reasoningEffort, string? storedEffort)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        AddMessage(1002UL, Attachment(13UL, "c.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));
        var startedAtUtc = DateTime.UtcNow;

        await RunJobAsync(reasoningEffort: reasoningEffort);

        await using var verify = NewContext();
        var annotations = await verify.MemeAnnotations
            .Include(a => a.MemeIndex)
            .OrderBy(a => a.AttachmentDiscordId)
            .ToListAsync();
        Assert.Equal([11UL, 12UL, 13UL], annotations.Select(a => a.AttachmentDiscordId));
        Assert.Equal(["Opis obrazka 1", "Opis obrazka 2", "Opis obrazka 3"], annotations.Select(a => a.DescriptionPl));
        Assert.All(annotations, a =>
        {
            Assert.Equal(a.AttachmentDiscordId, a.MemeIndex.AttachmentDiscordId);
            Assert.Equal(MemeIndexStatus.Indexed, a.MemeIndex.Status);
            Assert.Equal(ConfiguredModel, a.ModelId);
            Assert.Equal(OpenRouterClient.PromptVersion, a.PromptVersion);
            Assert.Equal(storedEffort, a.ReasoningEffort);
            Assert.Equal(["test", "mem"], a.Tags);
            Assert.NotNull(a.RawResponseJson);
            Assert.InRange(a.IndexedAtUtc, startedAtUtc.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        });
    }

    // Prod sends `reasoning.effort` only when OpenRouter:ReasoningEffort is set (#366);
    // unset, the model keeps its own default.
    [Theory]
    [InlineData("low")]
    [InlineData(null)]
    public async Task ExecuteAsync_ReasoningEffortSetting_ReachesEveryModelCall(string? reasoningEffort)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunJobAsync(reasoningEffort: reasoningEffort);

        Assert.Equal(2, _http.ModelRequests.Count);
        Assert.All(_http.ModelRequests, request => Assert.Equal((ConfiguredModel, reasoningEffort), request));
    }

    [Fact]
    public async Task ExecuteAsync_FullRun_StoresEverySchemaV2FieldOfTheModelOutput()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal(OpenRouterClient.PromptVersion, annotation.PromptVersion);
        StoredMemeOutput.AssertDefaultOutput(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
    }

    // "none" is the model's word for no platform. The column holds NULL, and the check
    // constraint would refuse the word.
    [Fact]
    public async Task ExecuteAsync_SourceNone_IsStoredAsNull()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.Overrides.Add((Png(1), "source", "none"));

        await RunJobAsync();

        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        Assert.Null((await verify.MemeAnnotations.SingleAsync()).Source);
    }

    // The acceptance criterion of #368, through the backfill writer.
    [Fact]
    public async Task ExecuteAsync_CutoutThatNamesAPerson_IsStoredWithoutThePerson()
    {
        AddMessage(1001UL, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        StoredMemeOutput.AssertCutoutWithoutThePerson(await verify.MemeAnnotations.SingleAsync());
    }

    // wojtus_query reads every table, the raw column too.
    [Fact]
    public async Task ExecuteAsync_CutoutThatNamesAPerson_StoresARawResponseWithoutTheName()
    {
        AddMessage(1001UL, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        StoredMemeOutput.AssertRawResponseWithoutTheName(await verify.MemeAnnotations.SingleAsync());
    }

    // The log is the only trace that the rule fired. It holds counts: a name there would undo the rule.
    [Fact]
    public async Task ExecuteAsync_CutoutThatNamesAPerson_LogsTheDroppedCountsAndNoName()
    {
        AddMessage(1001UL, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));

        await RunJobAsync();

        var (level, message) = Assert.Single(_indexerLog.Entries, IsCutoutRuleEntry);
        Assert.Equal(LogLevel.Information, level);
        // The fake's cut-out loses its one person, 3 tags, 1 template and 1 search phrase.
        Assert.Contains("dropped 1 people and 5 terms", message);
        Assert.All(_indexerLog.Entries, entry => Assert.All(
            FakeMemeHttpHandler.PersonName.Split(' '),
            word => Assert.DoesNotContain(word, entry.Message, StringComparison.OrdinalIgnoreCase)));
    }

    // The control: the same output, only the image kind differs. The rule is about cut-outs, not about people.
    [Fact]
    public async Task ExecuteAsync_NamedPersonOnAnotherImageKind_KeepsThePersonTheTagsAndTheVerbatimRawResponse()
    {
        AddMessage(1001UL, Attachment(11UL, "photo.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.NamedPersonFor.Add(Png(1));
        _http.Overrides.Add((Png(1), "source", null));

        await RunJobAsync();

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        StoredMemeOutput.AssertNamedPersonKept(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
        Assert.DoesNotContain(_indexerLog.Entries, IsCutoutRuleEntry);
    }

    // Nothing was dropped, so the provenance stays the model's own output.
    [Fact]
    public async Task ExecuteAsync_CutoutWithoutPeople_KeepsTheVerbatimRawResponse()
    {
        AddMessage(1001UL, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.CutoutFor.Add(Png(1));
        _http.Overrides.Add((Png(1), "people", new JsonArray()));
        _http.Overrides.Add((Png(1), "source", null));

        await RunJobAsync();

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal(MemeImageKind.CutoutFaceOrEmote, annotation.ImageKind);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
        Assert.DoesNotContain(_indexerLog.Entries, IsCutoutRuleEntry);
    }

    [Fact]
    public async Task ExecuteAsync_RerunOverTerminalRows_MakesNoModelCalls()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();
        Assert.Equal(1, _http.ModelCalls);

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        Assert.Equal(1, await verify.MemeIndex.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Rerun_KeepsOneAnnotationPerKey()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();
        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal((11UL, ConfiguredModel, OpenRouterClient.PromptVersion),
            (annotation.AttachmentDiscordId, annotation.ModelId, annotation.PromptVersion));
    }

    // Both halves of the key count: another model, or this model under an older prompt.
    [Theory]
    [InlineData("other/model", OpenRouterClient.PromptVersion)]
    [InlineData(ConfiguredModel, "legacy")]
    public async Task ExecuteAsync_IndexedRowWithoutConfiguredKeyAnnotation_GetsOneMoreAnnotation(
        string existingModel, string existingPromptVersion)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, attemptCount: 1), existingModel, existingPromptVersion);
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        Assert.Equal(1, row.AttemptCount);
        var annotations = await verify.MemeAnnotations.ToListAsync();
        Assert.Equal(2, annotations.Count);
        Assert.Contains(annotations, a => a.ModelId == existingModel && a.PromptVersion == existingPromptVersion);
        Assert.Contains(annotations, a => a.ModelId == ConfiguredModel && a.PromptVersion == OpenRouterClient.PromptVersion
            && a.DescriptionPl == "Opis obrazka 1" && a.RawResponseJson is not null);
    }

    [Fact]
    public async Task ExecuteSweepAsync_IndexedRowWithoutConfiguredKeyAnnotation_IsNotRevisited()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, attemptCount: 1), "other/model");
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunSweepAsync();

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        Assert.Equal("other/model", (await verify.MemeAnnotations.SingleAsync()).ModelId);
    }

    // The key is unique: a second INSERT of it would be a violation, so the writer must see the
    // annotation before it spends a download or a model call.
    [Theory]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Pending)]
    public async Task ExecuteAsync_RowAlreadyHasConfiguredKeyAnnotation_BecomesIndexedWithoutDownloadOrModelCall(
        MemeIndexStatus status)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, status, attemptCount: 2), ConfiguredModel);
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(1, await verify.MemeAnnotations.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ExtraAnnotationFailsOrIsRefused_IndexedRowIsNotDowngraded(bool refusal)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, attemptCount: 1), "other/model");
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        (refusal ? _http.RefusalFor : _http.TransientErrorFor).Add(Png(1));

        await RunJobAsync();

        // The model was asked: the row is still Indexed because of the rule, not because nothing ran.
        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("other/model", (await verify.MemeAnnotations.SingleAsync()).ModelId);
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
    }

    [Fact]
    public async Task ExecuteAsync_SameBytesTwice_DedupesWithoutSecondModelCall()
    {
        AddMessage(1001UL, Attachment(11UL, "original.png"));
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(7));
        _http.SetImage(12UL, Png(7));

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.All(rows, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));
        Assert.Equal(rows[0].ContentHash, rows[1].ContentHash);
        var annotations = await verify.MemeAnnotations.OrderBy(a => a.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 12UL], annotations.Select(a => a.AttachmentDiscordId));
        Assert.Equal(annotations[0].DescriptionPl, annotations[1].DescriptionPl);
        Assert.Equal((annotations[0].ModelId, annotations[0].PromptVersion), (annotations[1].ModelId, annotations[1].PromptVersion));
        // Provenance stays on the original: the copy carries no raw response.
        Assert.NotNull(annotations[0].RawResponseJson);
        Assert.Null(annotations[1].RawResponseJson);
    }

    // Driven by the sweep: it never revisits the Indexed original, so the repost is the only work.
    [Fact]
    public async Task ExecuteSweepAsync_RepostOfMemeWithTwoAnnotations_CopiesAllAnnotations()
    {
        AddMessage(1001UL, Attachment(11UL, "original.png"));
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        var originalIndexedAtUtc = DateTime.UtcNow.AddDays(-30);
        var original = SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7)));
        Annotate(original, "model/a", "legacy", descriptionPl: "Opis modelu A", reasoningEffort: "low", indexedAtUtc: originalIndexedAtUtc);
        Annotate(original, "model/b", descriptionPl: "Opis modelu B", indexedAtUtc: originalIndexedAtUtc);
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(7));

        await RunSweepAsync();

        Assert.Equal(0, _http.ModelCalls);
        await using var verify = NewContext();
        var repost = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Indexed, repost.Status);
        var copies = await verify.MemeAnnotations
            .Where(a => a.AttachmentDiscordId == 12UL)
            .OrderBy(a => a.ModelId)
            .ToListAsync();
        Assert.Equal(
            [("model/a", "legacy", "low", "Opis modelu A"), ("model/b", OpenRouterClient.PromptVersion, null, "Opis modelu B")],
            copies.Select(c => (c.ModelId, c.PromptVersion, c.ReasoningEffort, c.DescriptionPl)));
        Assert.All(copies, c =>
        {
            Assert.Equal(repost.Id, c.MemeIndexId);
            Assert.Null(c.RawResponseJson);
            // A copy is dated by the copy, not by the original's model call.
            Assert.True(c.IndexedAtUtc > originalIndexedAtUtc.AddDays(29));
        });
        Assert.Equal(2, await verify.MemeAnnotations.CountAsync(a => a.AttachmentDiscordId == 11UL && a.RawResponseJson != null));
    }

    [Fact]
    public async Task ExecuteAsync_SameBytesTwice_CopyCarriesTheSchemaV2FieldsOfTheOriginal()
    {
        AddMessage(1001UL, Attachment(11UL, "original.png"));
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(7));
        _http.SetImage(12UL, Png(7));

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var copy = await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 12UL);
        StoredMemeOutput.AssertDefaultOutput(copy);
        Assert.Null(copy.RawResponseJson);
    }

    // image_kind and language of an annotation written before schema v2 are unknown. A copy must
    // not turn that into TemplateMeme / Pl, the enums' zero values.
    [Fact]
    public async Task ExecuteSweepAsync_RepostOfAnnotationWrittenBeforeSchemaV2_KeepsImageKindAndLanguageNull()
    {
        AddMessage(1001UL, Attachment(11UL, "original.png"));
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7))), "model/a", "legacy");
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(7));

        await RunSweepAsync();

        Assert.Equal(0, _http.ModelCalls);
        await using var verify = NewContext();
        var copy = await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 12UL);
        Assert.Null(copy.ImageKind);
        Assert.Null(copy.Language);
        Assert.Null(copy.Franchise);
        Assert.Null(copy.Source);
        Assert.Empty(copy.Templates);
        Assert.Empty(copy.SearchPhrases);
        Assert.Empty(copy.PeopleNames);
        Assert.Equal("[]", copy.People);
    }

    // The original comes from a writer without the rule (an import, an older build). The copy is
    // a new write, so the rule holds for it.
    [Fact]
    public async Task ExecuteSweepAsync_RepostOfCutoutThatStillNamesAPerson_IsCopiedWithoutThePerson()
    {
        AddMessage(1001UL, Attachment(11UL, "original.png"));
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7))), "model/a", configure: a =>
        {
            a.ImageKind = MemeImageKind.CutoutFaceOrEmote;
            a.People = $$"""[{"name": "{{FakeMemeHttpHandler.PersonName}}", "evidence": "widely_recognized"}]""";
            a.PeopleNames = [FakeMemeHttpHandler.PersonName];
            a.Tags = [.. FakeMemeHttpHandler.NameTags, .. FakeMemeHttpHandler.NameFreeTags];
            a.Templates = [FakeMemeHttpHandler.PersonName, .. FakeMemeHttpHandler.NameFreeTemplates];
            a.SearchPhrases = ["kowalski emotka", .. FakeMemeHttpHandler.NameFreeSearchPhrases];
            a.Franchise = FakeMemeHttpHandler.Franchise;
        });
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(7));

        await RunSweepAsync();

        Assert.Equal(0, _http.ModelCalls);
        await using var verify = NewContext();
        var copy = await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 12UL);
        StoredMemeOutput.AssertCutoutWithoutThePerson(copy);
        Assert.Null(copy.RawResponseJson);
    }

    [Fact]
    public async Task ExecuteSweepAsync_TwoIndexedRowsShareTheHash_RepostCopiesFromTheOldest()
    {
        AddMessage(1001UL, Attachment(11UL, "newer.png"));
        AddMessage(1002UL, Attachment(12UL, "older.png"));
        AddMessage(1003UL, Attachment(13UL, "repost.png"));
        await _db.SaveChangesAsync();
        // The newer row goes in first, so it wins on id and on physical order: only
        // first_seen_utc makes the other row the oldest.
        var newer = SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7)));
        Annotate(newer, "model/b", descriptionPl: "z nowszego");
        Annotate(newer, "model/c", descriptionPl: "z nowszego");
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1002UL, 12UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7))), "model/a", descriptionPl: "z najstarszego");
        await _db.SaveChangesAsync();
        await _db.MemeIndex
            .Where(m => m.AttachmentDiscordId == 12UL)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.FirstSeenUtc, DateTime.UtcNow.AddDays(-30)));
        _http.SetImage(13UL, Png(7));

        await RunSweepAsync();

        Assert.Equal(0, _http.ModelCalls);
        await using var verify = NewContext();
        var copy = await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 13UL);
        Assert.Equal("model/a", copy.ModelId);
        Assert.Equal("z najstarszego", copy.DescriptionPl);
    }

    // The manual backfill revisits an Indexed repost for the configured writer's annotation. When
    // the original lacks that key too, the copy cannot finish the job: the model still has to run.
    [Fact]
    public async Task ExecuteAsync_IndexedRepostLacksConfiguredKeyAfterCopy_SavesTheCopyAndCallsTheModel()
    {
        // No attachment on the original's message: it is not a candidate, so only the repost is revisited.
        AddMessage(1001UL);
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        var original = SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7)));
        Annotate(original, "model/a");
        Annotate(original, "model/b", descriptionPl: "Opis modelu B");
        Annotate(SeedRow(1002UL, 12UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7)), attemptCount: 1), "model/a");
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(7));

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var annotations = await verify.MemeAnnotations
            .Where(a => a.AttachmentDiscordId == 12UL)
            .OrderBy(a => a.ModelId)
            .ToListAsync();
        Assert.Equal(["model/a", "model/b", ConfiguredModel], annotations.Select(a => a.ModelId));
        Assert.Equal("Opis modelu B", annotations[1].DescriptionPl);
        Assert.Null(annotations[1].RawResponseJson);
        Assert.NotNull(annotations[2].RawResponseJson);
    }

    // The usual repost in a second-model backfill: the original already has the configured
    // writer's annotation, so the copy finishes the job and no model call is paid.
    [Fact]
    public async Task ExecuteAsync_IndexedRepostWhoseOriginalHasConfiguredKey_CopiesItWithoutModelCall()
    {
        AddMessage(1001UL);
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        var original = SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7)));
        Annotate(original, "model/a");
        Annotate(original, ConfiguredModel, descriptionPl: "Opis z oryginału");
        Annotate(SeedRow(1002UL, 12UL, MemeIndexStatus.Indexed, contentHash: HashOf(Png(7)), attemptCount: 1), "model/a");
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(7));

        await RunJobAsync();

        Assert.Equal(0, _http.ModelCalls);
        await using var verify = NewContext();
        var repost = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Indexed, repost.Status);
        Assert.Null(repost.Error);
        Assert.Equal(1, repost.AttemptCount);
        var annotations = await verify.MemeAnnotations
            .Where(a => a.AttachmentDiscordId == 12UL)
            .OrderBy(a => a.ModelId)
            .ToListAsync();
        Assert.Equal(["model/a", ConfiguredModel], annotations.Select(a => a.ModelId));
        Assert.Equal("Opis z oryginału", annotations[1].DescriptionPl);
        Assert.Null(annotations[1].RawResponseJson);
    }

    // Postgres rejects the extra annotation (NUL byte). The recovery path must leave the Indexed
    // row alone: recording the rejection would charge it an attempt on every manual run.
    [Fact]
    public async Task ExecuteAsync_ExtraAnnotationRejectedBySave_IndexedRowIsLeftUntouched()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        Annotate(SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, attemptCount: 1), "other/model");
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.NulOcrFor.Add(Png(1));

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        Assert.Equal(1, row.AttemptCount);
        var annotation = await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 11UL);
        Assert.Equal("other/model", annotation.ModelId);
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
    }

    [Fact]
    public async Task ExecuteAsync_OutcomeMapping_SkippedVsFailed()
    {
        AddMessage(1001UL, Attachment(11UL, "refused.png"));
        AddMessage(1002UL, Attachment(12UL, "flaky.png"));
        AddMessage(1003UL, Attachment(13UL, "not-an-image.png"));
        AddMessage(1004UL, Attachment(14UL, "dead.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Encoding.ASCII.GetBytes("definitely not image bytes"));
        _http.DeadAttachments.Add(14UL);
        _http.RefusalFor.Add(Png(1));
        _http.TransientErrorFor.Add(Png(2));

        await RunJobAsync();

        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Skipped, byId[11UL].Status);
        Assert.StartsWith("model refusal", byId[11UL].Error);
        Assert.Equal(MemeIndexStatus.Failed, byId[12UL].Status);
        // Transient model errors refund the attempt (#293): only deterministic failures
        // may walk a row toward the sweep's permanent-abandonment cap.
        Assert.Equal(0, byId[12UL].AttemptCount);
        Assert.StartsWith("transient", byId[12UL].Error);
        Assert.Equal(MemeIndexStatus.Skipped, byId[13UL].Status);
        Assert.StartsWith("unsupported", byId[13UL].Error);
        Assert.Equal(MemeIndexStatus.Skipped, byId[14UL].Status);
        Assert.StartsWith("dead attachment", byId[14UL].Error);

        // Re-run retries ONLY the Failed row — and this time it succeeds.
        _http.TransientErrorFor.Clear();
        var callsBefore = _http.ModelCalls;
        await RunJobAsync();

        Assert.Equal(callsBefore + 1, _http.ModelCalls);
        await using var verify2 = NewContext();
        var retried = await verify2.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Indexed, retried.Status);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Null(retried.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PoisonRow_LandsFailed_RunContinues_CheckpointCompletes()
    {
        AddMessage(1001UL, Attachment(11UL, "fine.png"));
        AddMessage(1002UL, Attachment(12UL, "nul-byte.png"));
        AddMessage(1003UL, Attachment(13UL, "also-fine.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));
        _http.NulOcrFor.Add(Png(2));

        await RunJobAsync();

        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Indexed, byId[11UL].Status);
        Assert.Equal(MemeIndexStatus.Failed, byId[12UL].Status);
        Assert.StartsWith("poisoned: ", byId[12UL].Error);
        Assert.Equal(1, byId[12UL].AttemptCount);
        Assert.Equal(MemeIndexStatus.Indexed, byId[13UL].Status);
        // The rejected annotation went down with its save; the neighbours kept theirs.
        Assert.Equal([11UL, 13UL],
            await verify.MemeAnnotations.OrderBy(a => a.AttachmentDiscordId).Select(a => a.AttachmentDiscordId).ToListAsync());

        // The run outlived the poison row: checkpoint terminal, progress counted, cursor at the end.
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
        Assert.Equal(3, checkpoint.ProcessedCount);
        Assert.Equal(1003UL, checkpoint.LastProcessedId);

        // Deterministic failure: the sweep retries it, charges the attempt, and the cap abandons it.
        await RunSweepAsync();
        await using var verify2 = NewContext();
        var retried = await verify2.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Failed, retried.Status);
        Assert.Equal(2, retried.AttemptCount);
        Assert.False(await verify2.MemeAnnotations.AnyAsync(a => a.AttachmentDiscordId == 12UL));
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentRunIndexesSameAttachment_RecoveryKeepsItsIndexedRow()
    {
        AddMessage(1001UL, Attachment(11UL, "raced.png"));
        AddMessage(1002UL, Attachment(12UL, "fine.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        var racedMessageId = await _db.Messages.Where(m => m.DiscordId == 1001UL).Select(m => m.Id).SingleAsync();

        // A second run (admin trigger during the sweep) lands attachment 11 first; this run's insert
        // then hits the unique index on attachment_discord_id.
        var raced = false;
        _http.DuringModelCall = async () =>
        {
            if (raced)
                return;
            raced = true;
            await using var other = NewContext();
            other.MemeIndex.Add(new MemeIndexEntity
            {
                MessageId = racedMessageId,
                GuildDiscordId = GuildDiscordId,
                ChannelDiscordId = ChannelDiscordId,
                MessageDiscordId = 1001UL,
                AttachmentDiscordId = 11UL,
                FileName = "raced.png",
                FileSizeBytes = 123,
                Status = MemeIndexStatus.Indexed,
                AttemptCount = 1,
                Annotations =
                [
                    new MemeAnnotationEntity
                    {
                        AttachmentDiscordId = 11UL,
                        ModelId = "other/run",
                        PromptVersion = OpenRouterClient.PromptVersion,
                        IndexedAtUtc = DateTime.UtcNow,
                        DescriptionPl = "wygrany",
                        DescriptionEn = "winner",
                        OcrText = "",
                        RawResponseJson = "{}",
                    },
                ],
            });
            await other.SaveChangesAsync();
        };

        await RunJobAsync();

        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Indexed, byId[11UL].Status);
        // This run's own annotation was in the rejected save; the winner's row and annotation stand.
        Assert.Equal("other/run", (await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 11UL)).ModelId);
        Assert.Null(byId[11UL].Error);
        Assert.Equal(1, byId[11UL].AttemptCount);
        Assert.Equal(MemeIndexStatus.Indexed, byId[12UL].Status);
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
        Assert.Equal(2, checkpoint.ProcessedCount);
        Assert.Equal(1002UL, checkpoint.LastProcessedId);
    }

    // `required` in System.Text.Json is presence-only, so every one of these parses. Without the
    // guard the null reaches a NOT NULL column or the cut-out rule and takes the whole save down.
    [Theory]
    [InlineData("description_pl", "null")]
    [InlineData("description_en", "null")]
    [InlineData("ocr_text", "null")]
    [InlineData("tags", "null")]
    [InlineData("tags", """["test", null]""")]
    [InlineData("image_kind", "null")]
    [InlineData("templates", "null")]
    [InlineData("templates", "[null]")]
    [InlineData("people", "null")]
    [InlineData("people", "[null]")]
    [InlineData("people", """[{"name": null, "evidence": "name_visible"}]""")]
    [InlineData("search_phrases", "null")]
    [InlineData("search_phrases", "[null]")]
    [InlineData("language", "null")]
    public async Task ExecuteAsync_NullRequiredMetadataField_LandsFailedWithoutPoisoningTheRun(string member, string valueJson)
    {
        AddMessage(1001UL, Attachment(11UL, "null-field.png"));
        AddMessage(1002UL, Attachment(12UL, "fine.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.Overrides.Add((Png(1), member, JsonNode.Parse(valueJson)));

        await RunJobAsync();

        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Failed, byId[11UL].Status);
        Assert.Equal("model returned null for a required metadata field", byId[11UL].Error);
        Assert.Equal(1, byId[11UL].AttemptCount);
        Assert.Equal(MemeIndexStatus.Indexed, byId[12UL].Status);
        Assert.Equal([12UL], await verify.MemeAnnotations.Select(a => a.AttachmentDiscordId).ToListAsync());
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
    }

    // franchise and source are the two members the contract lets be null.
    [Theory]
    [InlineData("franchise")]
    [InlineData("source")]
    public async Task ExecuteAsync_NullFranchiseOrSource_IsIndexedWithANullColumn(string member)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.Overrides.Add((Png(1), member, null));

        await RunJobAsync();

        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Null(member == "franchise" ? annotation.Franchise : annotation.Source);
    }

    // A blank franchise is no franchise. Stored as text it would read as a value in every query.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ExecuteAsync_BlankFranchise_IsStoredAsNull(string franchise)
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.Overrides.Add((Png(1), "franchise", franchise));

        await RunJobAsync();

        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        Assert.Null((await verify.MemeAnnotations.SingleAsync()).Franchise);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshBatchFailure_MarksFailedAndLaterRunHeals()
    {
        // A transient refresh-urls failure (5xx/timeout) must not mark the
        // batch Skipped — Skipped is terminal and the memes would be silently
        // lost from search forever. It also must not burn a retry attempt:
        // the attachment itself was never actually processed.
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.RefreshFailuresRemaining = 1;

        await RunJobAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(MemeIndexStatus.Failed, r.Status);
            Assert.Equal(0, r.AttemptCount);
        });
        Assert.Equal(0, _http.ModelCalls);

        // Next run: refresh succeeds, both rows index normally.
        await RunJobAsync();

        await using var verify2 = NewContext();
        var healed = await verify2.MemeIndex.ToListAsync();
        Assert.All(healed, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));
        Assert.Equal(2, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteSweepAsync_RefreshBatchFailures_NeverExhaustAttemptCap()
    {
        // The sweep abandons rows at SweepMaxFailedAttempts — repeated batch
        // failures must stay below it so a flaky week can't orphan a meme.
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        for (var i = 0; i < MemeIndexingJob.SweepMaxFailedAttempts + 1; i++)
        {
            _http.RefreshFailuresRemaining = 1;
            await RunSweepAsync();
        }

        await RunSweepAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
    }

    [Fact]
    public async Task ExecuteAsync_UrlDeclinedByDiscord_StaysSkipped()
    {
        // Contrast case: a 2xx refresh response that omits the URL means the
        // attachment is genuinely gone — terminal Skip remains correct.
        AddMessage(1001UL, Attachment(11UL, "deleted.png"));
        await _db.SaveChangesAsync();
        _http.DeadAttachments.Add(11UL);

        await RunJobAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Skipped, row.Status);
        Assert.StartsWith("dead attachment", row.Error);
    }

    [Fact]
    public async Task ExecuteAsync_StrandedPendingRow_IsReprocessed()
    {
        // A mid-attachment interruption leaves a committed Pending row behind
        // (the executor's failure path flushes it). It must not be terminal.
        AddMessage(1001UL, Attachment(11UL, "stranded.png"));
        await _db.SaveChangesAsync();
        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = _db.Messages.Single(m => m.DiscordId == 1001UL).Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = 1001UL,
            AttachmentDiscordId = 11UL,
            FileName = "stranded.png",
            FileSizeBytes = 123,
            Status = MemeIndexStatus.Pending
        });
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Equal(1, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_InterruptedRun_ResumesFromMessageCursor()
    {
        AddMessage(1001UL, Attachment(11UL, "done-before-crash.png"));
        AddMessage(1002UL, Attachment(12UL, "after-cursor.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        // Simulate a SIGKILLed run: checkpoint left InProgress with the cursor
        // past message 1001 — the executor only honors the cursor in this state.
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            LastProcessedId = 1001UL,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await RunJobAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(12UL, row.AttachmentDiscordId);
        Assert.Equal(1, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_MaxImagesPerRun_CapsTheRunAndNextRunContinues()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        AddMessage(1002UL, Attachment(12UL, "b.png"));
        AddMessage(1003UL, Attachment(13UL, "c.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));

        await RunJobAsync(maxImagesPerRun: 2);

        await using var verify = NewContext();
        Assert.Equal(2, await verify.MemeIndex.CountAsync());
        Assert.Equal(2, _http.ModelCalls);

        await RunJobAsync(maxImagesPerRun: 2);

        await using var verify2 = NewContext();
        Assert.Equal(3, await verify2.MemeIndex.CountAsync());
        Assert.Equal(3, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_MetadataOversizedAttachment_SkippedWithoutDownload()
    {
        // Discord already told us the file size — an attachment over MaxImageBytes
        // must be pre-skipped from metadata, not buffered in full first.
        AddMessage(1001UL, Attachment(11UL, "huge.png", fileSize: 26L * 1024 * 1024));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Skipped, row.Status);
        Assert.StartsWith("unsupported: image too large", row.Error);
        Assert.Equal(0, _http.CdnRequests);
        Assert.Equal(0, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_DownloadExceedsBufferCap_SkippedNotRetriedForever()
    {
        // Metadata can lie small. The download client's buffer cap is the backstop —
        // and blowing it is deterministic, so it must be a terminal Skip, not a
        // transient Failure that every future sweep retries (and refunds) forever.
        AddMessage(1001UL, Attachment(11UL, "liar.png", fileSize: 10));
        await _db.SaveChangesAsync();
        var oversized = new byte[100];
        Png(1).CopyTo(oversized, 0);
        _http.SetImage(11UL, oversized);

        await RunJobAsync(maxImageBytes: 64);

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Skipped, row.Status);
        Assert.StartsWith("unsupported: image too large", row.Error);
        Assert.Equal(0, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_CapSplitsMultiAttachmentMessage_ResumeDoesNotSkipSiblings()
    {
        // MaxImagesPerRun can cut a multi-attachment message in half. The resume
        // cursor must NOT advance to that message: a crash before the run's
        // Completed flip would otherwise make the resumed run skip the siblings.
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunJobAsync(maxImagesPerRun: 1);

        // Simulate a SIGKILL between the last per-item save and MarkCompleted:
        // status stays InProgress, so the next run honors the saved cursor.
        await using (var crash = NewContext())
        {
            await crash.BackfillCheckpoints
                .Where(c => c.Type == BackfillType.MemeIndex)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, BackfillStatus.InProgress));
        }

        await RunJobAsync();

        await using var verify = NewContext();
        var sibling = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Indexed, sibling.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MidFlightResume_NeverReportsProcessedAboveTotal()
    {
        // A resumed run keeps accumulating ProcessedCount — TotalCount must
        // include that prior progress or the status endpoint shows processed > total.
        AddMessage(1001UL, Attachment(11UL, "done-before-crash.png"));
        AddMessage(1002UL, Attachment(12UL, "after-cursor.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(2));

        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            LastProcessedId = 1001UL,
            ProcessedCount = 5,
            TotalCount = 6,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await RunJobAsync();

        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(6, checkpoint.ProcessedCount);
        Assert.Equal(6, checkpoint.TotalCount);
    }

    private Task RunJobAsync(
        int maxImagesPerRun = 500, int maxImageBytes = DefaultMaxImageBytes, string? reasoningEffort = null)
        => RunAsync(maxImagesPerRun, maxImageBytes, sweep: false, reasoningEffort);

    private Task RunSweepAsync() => RunAsync(maxImagesPerRun: 500, DefaultMaxImageBytes, sweep: true);

    private async Task RunAsync(int maxImagesPerRun, int maxImageBytes, bool sweep, string? reasoningEffort = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // A closed registration wins over AddLogging's open ILogger<>.
        services.AddSingleton(_indexerLog.For<MemeAttachmentIndexer>());
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = [ChannelDiscordId];
            o.MaxImagesPerRun = maxImagesPerRun;
            o.MaxImageBytes = maxImageBytes;
        });
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = ConfiguredModel;
            o.ReasoningEffort = reasoningEffort;
            o.RequestDelayMs = 0;
        });
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_http, maxImageBytes));
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeAttachmentIndexer>();
        services.AddScoped<BackfillJobExecutor>();
        services.AddScoped<MemeIndexingJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<MemeIndexingJob>();
        if (sweep)
            await job.ExecuteSweepAsync(GuildDiscordId, CancellationToken.None);
        else
            await job.ExecuteAsync(GuildDiscordId, CancellationToken.None);
    }

    private void AddMessage(ulong discordId, params string[] attachments)
    {
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    // Call after the message is saved: the status row needs the message's database id.
    private MemeIndexEntity SeedRow(
        ulong messageDiscordId, ulong attachmentId, MemeIndexStatus status, string? contentHash = null, int attemptCount = 0)
    {
        var row = new MemeIndexEntity
        {
            MessageId = _db.Messages.Local.Single(m => m.DiscordId == messageDiscordId).Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = messageDiscordId,
            AttachmentDiscordId = attachmentId,
            FileName = $"seeded-{attachmentId}.png",
            FileSizeBytes = 123,
            ContentHash = contentHash,
            Status = status,
            Error = status is MemeIndexStatus.Failed or MemeIndexStatus.Skipped ? "seeded by test" : null,
            AttemptCount = attemptCount
        };
        _db.MemeIndex.Add(row);
        return row;
    }

    private static void Annotate(
        MemeIndexEntity row,
        string modelId,
        string promptVersion = OpenRouterClient.PromptVersion,
        string descriptionPl = "Opis z seeda",
        string? reasoningEffort = null,
        DateTime? indexedAtUtc = null,
        Action<MemeAnnotationEntity>? configure = null)
    {
        var annotation = new MemeAnnotationEntity
        {
            AttachmentDiscordId = row.AttachmentDiscordId,
            ModelId = modelId,
            PromptVersion = promptVersion,
            ReasoningEffort = reasoningEffort,
            IndexedAtUtc = indexedAtUtc ?? DateTime.UtcNow,
            DescriptionPl = descriptionPl,
            DescriptionEn = "Seeded description",
            OcrText = "",
            Tags = ["seed"],
            RawResponseJson = "{}"
        };
        configure?.Invoke(annotation);
        row.Annotations.Add(annotation);
    }

    private static bool IsCutoutRuleEntry((LogLevel Level, string Message) entry) =>
        entry.Message.Contains("Cut-out rule applied", StringComparison.Ordinal);

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
    private static string Attachment(ulong id, string fileName, long fileSize = 123) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{ChannelDiscordId}/{id}/{fileName}?ex=expired\",\"FileName\":\"{fileName}\",\"FileSize\":{fileSize}}}";

    // Distinct valid-PNG-magic payloads (≥12 bytes for the sniffer).
    private static byte[] Png(byte seed) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, seed, seed, seed];

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}

internal sealed class FakeMemeHttpHandler : HttpMessageHandler
{
    // The person the model names for CutoutFor / NamedPersonFor: in people, in NameTags, in one
    // template and in one search phrase. Never in the descriptions or the OCR text.
    public const string PersonName = "Jan Kowalski";
    public static readonly string[] NameTags = ["jan kowalski", "kowalski", "jan_kowalski"];
    public static readonly string[] NameFreeTags = ["emotka", "twarz"];
    public static readonly string[] NameFreeTemplates = ["wojak"];
    public static readonly string[] NameFreeSearchPhrases = ["śmieszna mina"];
    // On every output. It names nobody, so the cut-out rule must leave it alone.
    public const string Franchise = "Wiedźmin";

    private readonly Dictionary<ulong, byte[]> _imagesByAttachment = [];

    public HashSet<ulong> DeadAttachments { get; } = [];
    public List<byte[]> RefusalFor { get; } = [];
    public List<byte[]> TransientErrorFor { get; } = [];
    // Valid JSON, but Postgres rejects U+0000 in text columns — the save throws (#311).
    public List<byte[]> NulOcrFor { get; } = [];
    // A cut-out that names PersonName all the same — the output the cut-out rule exists for (#368).
    public List<byte[]> CutoutFor { get; } = [];
    // The same output on an image kind the rule does not cover.
    public List<byte[]> NamedPersonFor { get; } = [];
    // Replaces one member of the output for one image: a null, an empty list, another source.
    public List<(byte[] Image, string Member, JsonNode? Value)> Overrides { get; } = [];
    // Runs mid model call — after the job added its row, before it saves (#311 concurrent-run race).
    public Func<Task>? DuringModelCall { get; set; }
    public int RefreshFailuresRemaining { get; set; }
    // Model id and `reasoning.effort` of every model call; the effort is null when the field was not sent.
    public ConcurrentQueue<(string Model, string? ReasoningEffort)> ModelRequests { get; } = new();
    public int ModelCalls { get; private set; }
    public int CdnRequests { get; private set; }

    public void SetImage(ulong attachmentId, byte[] bytes) => _imagesByAttachment[attachmentId] = bytes;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("attachments/refresh-urls", StringComparison.Ordinal))
            return await HandleRefreshAsync(request, cancellationToken);

        if (path.EndsWith("chat/completions", StringComparison.Ordinal))
            return await HandleModelAsync(request, cancellationToken);

        return HandleCdn(request);
    }

    private async Task<HttpResponseMessage> HandleRefreshAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (RefreshFailuresRemaining > 0)
        {
            RefreshFailuresRemaining--;
            return Json(HttpStatusCode.InternalServerError, """{"message":"upstream exploded"}""");
        }

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var urls = JsonDocument.Parse(body).RootElement.GetProperty("attachment_urls");

        var refreshed = urls.EnumerateArray()
            .Select(u => u.GetString()!)
            .Where(u => !DeadAttachments.Contains(AttachmentIdOf(u)))
            .Select(u => new { original = u, refreshed = u + "?sig=fresh" })
            .ToList();

        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { refreshed_urls = refreshed }));
    }

    private HttpResponseMessage HandleCdn(HttpRequestMessage request)
    {
        CdnRequests++;
        var id = AttachmentIdOf(request.RequestUri!.AbsoluteUri);
        if (!_imagesByAttachment.TryGetValue(id, out var bytes))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }

    private async Task<HttpResponseMessage> HandleModelAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var imageBytes = ExtractImageBytes(body);
        ModelRequests.Enqueue(ExtractModelAndEffort(body));
        ModelCalls++;
        if (DuringModelCall is not null)
            await DuringModelCall();

        if (TransientErrorFor.Any(b => b.AsSpan().SequenceEqual(imageBytes)))
            return Json(HttpStatusCode.InternalServerError, """{"error":"upstream exploded"}""");

        if (RefusalFor.Any(b => b.AsSpan().SequenceEqual(imageBytes)))
            return Json(HttpStatusCode.OK,
                """{"choices":[{"message":{"content":null,"refusal":"safety"},"finish_reason":"stop"}]}""");

        var response = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = MetadataJsonFor(imageBytes), refusal = (string?)null }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 100, completion_tokens = 50, cost = 0.0016m }
        });
        return Json(HttpStatusCode.OK, response);
    }

    // The schema v2 output the model sends for one image. Every member holds a value by default,
    // so a column the writer forgets shows up as a difference.
    public string MetadataJsonFor(byte[] imageBytes)
    {
        bool IsThisImage(byte[] other) => other.AsSpan().SequenceEqual(imageBytes);

        var isCutout = CutoutFor.Any(IsThisImage);
        var namesPerson = isCutout || NamedPersonFor.Any(IsThisImage);
        var metadata = new Dictionary<string, object?>
        {
            ["description_pl"] = $"Opis obrazka {imageBytes[^1]}",
            ["description_en"] = $"Description of image {imageBytes[^1]}",
            ["ocr_text"] = NulOcrFor.Any(IsThisImage) ? "top text \0 bottom text" : "",
            ["tags"] = namesPerson ? [.. NameTags, .. NameFreeTags] : new[] { "test", "mem" },
            ["image_kind"] = isCutout ? "cutout_face_or_emote" : "template_meme",
            ["templates"] = namesPerson ? [PersonName, .. NameFreeTemplates] : new[] { "drake" },
            ["people"] = new[] { new { name = namesPerson ? PersonName : "Adam Małysz", evidence = "widely_recognized" } },
            ["search_phrases"] = namesPerson ? ["kowalski emotka", .. NameFreeSearchPhrases] : new[] { "mem testowy", "test meme" },
            ["franchise"] = Franchise,
            ["source"] = "kwejk",
            ["language"] = "pl",
        };
        foreach (var (_, member, value) in Overrides.Where(o => IsThisImage(o.Image)))
            metadata[member] = value;

        return JsonSerializer.Serialize(metadata);
    }

    private static byte[] ExtractImageBytes(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var dataUrl = doc.RootElement.GetProperty("messages")[1].GetProperty("content")[0]
            .GetProperty("image_url").GetProperty("url").GetString()!;
        return Convert.FromBase64String(dataUrl[(dataUrl.IndexOf("base64,", StringComparison.Ordinal) + 7)..]);
    }

    private static (string Model, string? ReasoningEffort) ExtractModelAndEffort(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var effort = doc.RootElement.TryGetProperty("reasoning", out var reasoning)
            ? reasoning.GetProperty("effort").GetString()
            : null;
        return (doc.RootElement.GetProperty("model").GetString()!, effort);
    }

    // .../attachments/{channelId}/{attachmentId}/{fileName}
    private static ulong AttachmentIdOf(string url)
    {
        var segments = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return ulong.Parse(segments[^2]);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

// What a writer must store for each FakeMemeHttpHandler output. Shared: the backfill, the sweep
// and the live hook all have to store the same thing.
internal static class StoredMemeOutput
{
    public static void AssertDefaultOutput(MemeAnnotationEntity annotation)
    {
        Assert.Equal(["test", "mem"], annotation.Tags);
        Assert.Equal(MemeImageKind.TemplateMeme, annotation.ImageKind);
        Assert.Equal(["drake"], annotation.Templates);
        Assert.Equal(["mem testowy", "test meme"], annotation.SearchPhrases);
        Assert.Equal(["Adam Małysz"], annotation.PeopleNames);
        AssertPeopleColumn(annotation, "Adam Małysz");
        Assert.Equal(FakeMemeHttpHandler.Franchise, annotation.Franchise);
        Assert.Equal("kwejk", annotation.Source);
        Assert.Equal(MemeLanguage.Pl, annotation.Language);
    }

    // Nothing that search reads names the person any more.
    public static void AssertCutoutWithoutThePerson(MemeAnnotationEntity annotation)
    {
        Assert.Equal(MemeImageKind.CutoutFaceOrEmote, annotation.ImageKind);
        Assert.Equal("[]", annotation.People);
        Assert.Empty(annotation.PeopleNames);
        Assert.Equal(FakeMemeHttpHandler.NameFreeTags, annotation.Tags);
        Assert.Equal(FakeMemeHttpHandler.NameFreeTemplates, annotation.Templates);
        Assert.Equal(FakeMemeHttpHandler.NameFreeSearchPhrases, annotation.SearchPhrases);
        Assert.Equal(FakeMemeHttpHandler.Franchise, annotation.Franchise);
        AssertNoNameIn(annotation.SearchText);
    }

    public static void AssertRawResponseWithoutTheName(MemeAnnotationEntity annotation)
    {
        Assert.NotNull(annotation.RawResponseJson);
        AssertNoNameIn(annotation.RawResponseJson);

        // Still the contract's shape: the sanitised metadata, not an empty object.
        using var raw = JsonDocument.Parse(annotation.RawResponseJson);
        Assert.Equal("cutout_face_or_emote", raw.RootElement.GetProperty("image_kind").GetString());
        Assert.Equal(0, raw.RootElement.GetProperty("people").GetArrayLength());
        Assert.Equal(FakeMemeHttpHandler.NameFreeTags, raw.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.StartsWith("Opis obrazka", raw.RootElement.GetProperty("description_pl").GetString());
    }

    public static void AssertNamedPersonKept(MemeAnnotationEntity annotation)
    {
        Assert.Equal(MemeImageKind.TemplateMeme, annotation.ImageKind);
        Assert.Equal([FakeMemeHttpHandler.PersonName], annotation.PeopleNames);
        AssertPeopleColumn(annotation, FakeMemeHttpHandler.PersonName);
        Assert.Equal([.. FakeMemeHttpHandler.NameTags, .. FakeMemeHttpHandler.NameFreeTags], annotation.Tags);
        Assert.Equal([FakeMemeHttpHandler.PersonName, .. FakeMemeHttpHandler.NameFreeTemplates], annotation.Templates);
        Assert.Equal(["kowalski emotka", .. FakeMemeHttpHandler.NameFreeSearchPhrases], annotation.SearchPhrases);
    }

    // jsonb keeps the value, not the text: key order and spacing change, the content must not.
    public static void AssertVerbatimRawResponse(MemeAnnotationEntity annotation, string modelOutput) =>
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(modelOutput), JsonNode.Parse(annotation.RawResponseJson!)),
            $"raw_response_json is not the model's output: {annotation.RawResponseJson}");

    // One { name, evidence } object, the evidence under its JSON name.
    private static void AssertPeopleColumn(MemeAnnotationEntity annotation, string name)
    {
        using var people = JsonDocument.Parse(annotation.People);
        var person = Assert.Single(people.RootElement.EnumerateArray());
        Assert.Equal(["evidence", "name"], person.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(name, person.GetProperty("name").GetString());
        Assert.Equal("widely_recognized", person.GetProperty("evidence").GetString());
    }

    private static void AssertNoNameIn(string text) =>
        Assert.All(FakeMemeHttpHandler.PersonName.Split(' '), word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler, long maxImageBytes = 25 * 1024 * 1024) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = name switch
            {
                AttachmentUrlRefreshService.HttpClientName => new Uri("https://discord.test/api/v10/"),
                OpenRouterClient.HttpClientName => new Uri("https://openrouter.test/api/v1/"),
                _ => null
            }
        };
        // Mirrors the prod discord-cdn client's download hard cap.
        if (name == MemeBenchmarkJob.DownloadHttpClientName)
            client.MaxResponseContentBufferSize = maxImageBytes;
        return client;
    }
}
