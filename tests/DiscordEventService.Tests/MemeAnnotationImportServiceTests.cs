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

// #369: the import of annotations produced outside the bot. The real service, the real indexer
// and the real MemeSampleService over Postgres; only HTTP is faked, and the import must not use it.
public sealed class MemeAnnotationImportServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;
    private const ulong OtherChannelDiscordId = 99UL;
    // What the bot itself would write under: OpenRouter:Model.
    private const string ConfiguredModel = "test/model";
    // What the import writes under: free text, named by the external writer.
    private const string ImportModel = "anthropic/claude-opus-5.5";

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private ChannelEntity _otherChannel = null!;
    private UserEntity _author = null!;
    // Two jobs: the HTTP fake for the tests that also run the indexing job, and the source of
    // the metadata JSON (MetadataJsonFor), so StoredMemeOutput's assertions fit an import too.
    private FakeMemeHttpHandler _http = null!;
    private readonly RecordingLogger _log = new();

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
        _otherChannel = new ChannelEntity { DiscordId = OtherChannelDiscordId, GuildId = _guild.Id, Name = "general", Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.AddRange(_channel, _otherChannel);
        _db.Users.Add(_author);
        await _db.SaveChangesAsync();

        _http = new FakeMemeHttpHandler();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // The first acceptance criterion of #369.
    [Fact]
    public async Task ImportAsync_SameKeyAndMetadataTwice_KeepsOneRowAndSkipsTheSecond()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var first = await ImportAsync(Item(11UL, Metadata(1)));
        var second = await ImportAsync(Item(11UL, Metadata(1)));

        var imported = Assert.Single(first.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Imported, imported.Outcome);
        Assert.False(imported.Overwritten);
        Assert.Null(imported.Reason);
        Assert.Equal((1, 0, 0, 0), Counts(first));

        var skipped = Assert.Single(second.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Skipped, skipped.Outcome);
        Assert.False(skipped.Overwritten);
        Assert.False(string.IsNullOrWhiteSpace(skipped.Reason));
        Assert.Equal((0, 0, 1, 0), Counts(second));

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal((11UL, ImportModel, OpenRouterClient.PromptVersion),
            (annotation.AttachmentDiscordId, annotation.ModelId, annotation.PromptVersion));
        Assert.Equal(1, await verify.MemeIndex.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_NewKey_StoresEverySchemaV2FieldAndTheMetadataAsSent()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        await ImportAsync(Item(11UL, Metadata(1)));

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal("Opis obrazka 1", annotation.DescriptionPl);
        Assert.Equal("Description of image 1", annotation.DescriptionEn);
        Assert.Equal("", annotation.OcrText);
        StoredMemeOutput.AssertDefaultOutput(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
    }

    // An external writer has no strict schema. A member outside the contract is not validated,
    // so it must not reach the raw column: it could carry a name past the cut-out rule.
    [Fact]
    public async Task ImportAsync_MembersOutsideTheContract_AreNotStoredInTheRawColumn()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        var metadata = Metadata(1);
        metadata["image_kind"] = "cutout_face_or_emote";
        metadata["people"] = new JsonArray();
        var sent = metadata.DeepClone().AsObject();
        sent["identity_guess"] = FakeMemeHttpHandler.PersonName;
        sent["confidence"] = new JsonObject { ["who"] = FakeMemeHttpHandler.PersonName };

        var response = await ImportAsync(Item(11UL, sent));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.DoesNotContain("identity_guess", annotation.RawResponseJson);
        Assert.DoesNotContain("Kowalski", annotation.RawResponseJson);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, metadata.ToJsonString());
    }

    // The same one level down: a `people` element is { name, evidence } and nothing else.
    [Fact]
    public async Task ImportAsync_MemberOutsideTheContractInsideAPerson_IsNotStoredInTheRawColumnOrThePeopleColumn()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        var sent = Metadata(1);
        sent["people"]![0]!["also_looks_like"] = FakeMemeHttpHandler.PersonName;

        var response = await ImportAsync(Item(11UL, sent));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.DoesNotContain("Kowalski", annotation.RawResponseJson);
        Assert.DoesNotContain("Kowalski", annotation.People);
        StoredMemeOutput.AssertDefaultOutput(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
    }

    // The same annotation sent again is the same annotation: the JSON text and the time of the
    // run are not part of its content.
    [Fact]
    public async Task ImportAsync_SameMetadataWithOtherKeyOrderAndTimestamp_IsSkippedAndKeepsTheStoredTimestamp()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        await ImportAsync(Item(11UL, Metadata(1), indexedAtUtc: "2026-09-01T12:00:00Z"));
        var reordered = new JsonObject(Metadata(1).Reverse().Select(m => KeyValuePair.Create(m.Key, m.Value?.DeepClone())));

        var response = await ImportAsync(Item(11UL, reordered, indexedAtUtc: "2026-09-20T08:00:00Z"));

        Assert.Equal(MemeAnnotationImportOutcome.Skipped, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), annotation.IndexedAtUtc);
    }

    // "A re-import overwrites": one row, updated in place.
    [Fact]
    public async Task ImportAsync_SameKeyDifferentMetadata_OverwritesEveryMetadataColumnInPlace()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        await ImportAsync(Item(11UL, Metadata(1), indexedAtUtc: "2026-09-01T12:00:00Z", reasoningEffort: "low"));
        MemeAnnotationEntity before;
        await using (var read = NewContext())
            before = await read.MemeAnnotations.SingleAsync();
        var replacement = OtherMetadata();

        var response = await ImportAsync(
            Item(11UL, replacement, indexedAtUtc: "2026-09-20T08:00:00Z", reasoningEffort: "high"));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Imported, result.Outcome);
        Assert.True(result.Overwritten);
        Assert.Equal((1, 1, 0, 0), Counts(response));

        await using var verify = NewContext();
        var after = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.MemeIndexId, after.MemeIndexId);
        Assert.Equal(before.FirstSeenUtc, after.FirstSeenUtc);
        Assert.True(after.LastUpdatedUtc > before.LastUpdatedUtc,
            $"last_updated_utc did not move: {before.LastUpdatedUtc:O} -> {after.LastUpdatedUtc:O}");
        Assert.Equal((ImportModel, OpenRouterClient.PromptVersion), (after.ModelId, after.PromptVersion));
        Assert.Equal("high", after.ReasoningEffort);
        Assert.Equal(new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), after.IndexedAtUtc);
        AssertOtherMetadataStored(after);
        StoredMemeOutput.AssertVerbatimRawResponse(after, replacement.ToJsonString());
    }

    // The "same content" check must look at every member: one it forgets makes a corrected
    // annotation come back as "skipped" and the old value stay.
    [Theory]
    [InlineData("description_pl", "\"Inny opis\"")]
    [InlineData("description_en", "\"Another description\"")]
    [InlineData("ocr_text", "\"napis\"")]
    [InlineData("tags", "[\"test\"]")]
    [InlineData("tags", "[\"mem\",\"test\"]")]
    [InlineData("image_kind", "\"comic\"")]
    [InlineData("templates", "[]")]
    [InlineData("people", "[]")]
    [InlineData("people", "[{\"name\":\"Adam Małysz\",\"evidence\":\"name_visible\"}]")]
    [InlineData("people", "[{\"name\":\"Robert Kubica\",\"evidence\":\"widely_recognized\"}]")]
    [InlineData("search_phrases", "[\"mem testowy\"]")]
    [InlineData("franchise", "null")]
    [InlineData("source", "\"jbzd\"")]
    [InlineData("source", "null")]
    [InlineData("language", "\"en\"")]
    public async Task ImportAsync_SameKeyWithOneMemberChanged_Overwrites(string member, string newValueJson)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        await ImportAsync(Item(11UL, Metadata(1)));
        var changed = Metadata(1);
        changed[member] = JsonNode.Parse(newValueJson);

        var response = await ImportAsync(Item(11UL, changed));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Imported, result.Outcome);
        Assert.True(result.Overwritten);
        await using var verify = NewContext();
        // The raw column holds the contract's own serialization: no source is written as "none".
        changed["source"] ??= MemeSources.None;
        StoredMemeOutput.AssertVerbatimRawResponse(await verify.MemeAnnotations.SingleAsync(), changed.ToJsonString());
    }

    // reasoning_effort is provenance, not part of the key: another effort is another run of the
    // same writer, so it replaces the stored annotation.
    [Fact]
    public async Task ImportAsync_SameKeyAndMetadataWithOtherReasoningEffort_Overwrites()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        await ImportAsync(Item(11UL, Metadata(1), reasoningEffort: "low"));

        var response = await ImportAsync(Item(11UL, Metadata(1), reasoningEffort: "high"));

        Assert.True(Assert.Single(response.Items).Overwritten);
        await using var verify = NewContext();
        Assert.Equal("high", (await verify.MemeAnnotations.SingleAsync()).ReasoningEffort);
    }

    // Blank and absent are the same stored value (NULL), so the re-import is not a change.
    [Fact]
    public async Task ImportAsync_BlankFranchiseAndSourceNone_AreStoredAsNullAndTheReimportIsSkipped()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        var metadata = Metadata(1);
        metadata["franchise"] = "  ";
        metadata["source"] = MemeSources.None;

        var first = await ImportAsync(Item(11UL, metadata));
        var second = await ImportAsync(Item(11UL, metadata.DeepClone().AsObject()));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(first.Items).Outcome);
        Assert.Equal(MemeAnnotationImportOutcome.Skipped, Assert.Single(second.Items).Outcome);
        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Null(annotation.Franchise);
        Assert.Null(annotation.Source);
    }

    // The second acceptance criterion: the attachment must be an image in a configured meme channel.
    [Theory]
    [InlineData(404UL)] // no such attachment anywhere
    [InlineData(21UL)]  // an image, but in a channel outside MemeIndex:ChannelIds
    [InlineData(31UL)]  // an image in the meme channel, on a deleted message
    [InlineData(41UL)]  // in the meme channel, but not an image file
    public async Task ImportAsync_AttachmentThatIsNotALiveImageInAMemeChannel_IsRejectedAndWritesNothing(ulong attachmentId)
    {
        AddMessage(1002UL, _otherChannel, Attachment(21UL, "elsewhere.png"));
        AddMessage(1003UL, _channel, deleted: true, Attachment(31UL, "deleted.png"));
        AddMessage(1004UL, _channel, Attachment(41UL, "clip.mp4"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(attachmentId, Metadata(1)));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Rejected, result.Outcome);
        Assert.Equal(attachmentId.ToString(), result.AttachmentDiscordId);
        Assert.Contains("not an image in a configured meme channel", result.Reason);
        Assert.Equal((0, 0, 0, 1), Counts(response));
        await AssertNothingStoredAsync();
    }

    // The third acceptance criterion, and the reason the import goes through the indexer's one
    // write seam (#368): one leaky writer is enough to bring a guessed name back into search.
    [Fact]
    public async Task ImportAsync_CutoutThatNamesAPerson_IsStoredWithoutThePerson()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.CutoutFor.Add(Png(1));

        var response = await ImportAsync(Item(11UL, Metadata(1)));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
        StoredMemeOutput.AssertCutoutWithoutThePerson(await verify.MemeAnnotations.SingleAsync());
    }

    // wojtus_query reads every table, the raw column too.
    [Fact]
    public async Task ImportAsync_CutoutThatNamesAPerson_StoresARawResponseWithoutTheName()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.CutoutFor.Add(Png(1));

        await ImportAsync(Item(11UL, Metadata(1)));

        await using var verify = NewContext();
        StoredMemeOutput.AssertRawResponseWithoutTheName(await verify.MemeAnnotations.SingleAsync());
    }

    // The log is the only trace that the rule fired. It holds counts: a name there would undo the rule.
    [Fact]
    public async Task ImportAsync_CutoutThatNamesAPerson_LogsTheDroppedCountsAndNoName()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.CutoutFor.Add(Png(1));

        await ImportAsync(Item(11UL, Metadata(1)));

        var (level, message) = Assert.Single(_log.Entries, IsCutoutRuleEntry);
        Assert.Equal(LogLevel.Information, level);
        // The fake's cut-out loses its one person, 3 tags, 1 template and 1 search phrase.
        Assert.Contains("dropped 1 people and 5 terms", message);
        // Both loggers write to _log: the indexer's and the import service's.
        Assert.All(_log.Entries, entry => Assert.All(
            FakeMemeHttpHandler.PersonName.Split(' '),
            word => Assert.DoesNotContain(word, entry.Message, StringComparison.OrdinalIgnoreCase)));
    }

    // Compared after the rule: the stored row has no people, the item has one, and it is still
    // the same annotation.
    [Fact]
    public async Task ImportAsync_CutoutThatNamesAPersonTwice_IsSkippedTheSecondTime()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.CutoutFor.Add(Png(1));
        await ImportAsync(Item(11UL, Metadata(1)));

        var response = await ImportAsync(Item(11UL, Metadata(1)));

        Assert.Equal(MemeAnnotationImportOutcome.Skipped, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        StoredMemeOutput.AssertCutoutWithoutThePerson(await verify.MemeAnnotations.SingleAsync());
    }

    // The log line says "the rule changed what was stored". A skipped re-import stores nothing,
    // so it must not log the line again: 12k re-sent items would be 12k false entries.
    [Fact]
    public async Task ImportAsync_CutoutThatNamesAPersonTwice_LogsTheCutoutRuleOnlyForTheWrite()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "cutout.png"));
        await _db.SaveChangesAsync();
        _http.CutoutFor.Add(Png(1));
        await ImportAsync(Item(11UL, Metadata(1)));
        Assert.Single(_log.Entries, IsCutoutRuleEntry);
        _log.Entries.Clear();

        var response = await ImportAsync(Item(11UL, Metadata(1)));

        Assert.Equal(MemeAnnotationImportOutcome.Skipped, Assert.Single(response.Items).Outcome);
        Assert.DoesNotContain(_log.Entries, IsCutoutRuleEntry);
    }

    // "skipped" is about the annotation only. The lifecycle row can have gone to Failed or Skipped
    // since (an API run that failed on it): the meme has an annotation, so it becomes findable again.
    [Theory]
    [InlineData(MemeIndexStatus.Pending)]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Skipped)]
    public async Task ImportAsync_SkippedReimportOverARowThatIsNotIndexed_StillMakesTheRowIndexed(MemeIndexStatus status)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        await ImportAsync(Item(11UL, Metadata(1)));
        await _db.MemeIndex.ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, status)
            .SetProperty(m => m.Error, "set by test")
            .SetProperty(m => m.AttemptCount, 2));

        var response = await ImportAsync(Item(11UL, Metadata(1)));

        Assert.Equal(MemeAnnotationImportOutcome.Skipped, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(1, await verify.MemeAnnotations.CountAsync());
    }

    // The control: the same metadata, only the image kind differs.
    [Fact]
    public async Task ImportAsync_NamedPersonOnAnotherImageKind_KeepsThePersonTheTagsAndTheVerbatimRawResponse()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "photo.png"));
        await _db.SaveChangesAsync();
        _http.NamedPersonFor.Add(Png(1));

        await ImportAsync(Item(11UL, Metadata(1)));

        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        StoredMemeOutput.AssertNamedPersonKept(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, _http.MetadataJsonFor(Png(1)));
        Assert.DoesNotContain(_log.Entries, IsCutoutRuleEntry);
    }

    // Production has no lifecycle rows before the first import (#371 runs before any API backfill).
    [Fact]
    public async Task ImportAsync_AttachmentWithoutLifecycleRow_CreatesAnIndexedRowWithoutContentHash()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        var messageId = _db.Messages.Local.Single().Id;

        await ImportAsync(Item(11UL, Metadata(1)));

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        // The import downloads nothing: no bytes, no hash, no attempt.
        Assert.Null(row.ContentHash);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(messageId, row.MessageId);
        Assert.Equal((GuildDiscordId, ChannelDiscordId, 1001UL, 11UL),
            (row.GuildDiscordId, row.ChannelDiscordId, row.MessageDiscordId, row.AttachmentDiscordId));
        Assert.Equal("a.png", row.FileName);
        Assert.Equal(123, row.FileSizeBytes);
        Assert.Equal(row.Id, (await verify.MemeAnnotations.SingleAsync()).MemeIndexId);
        Assert.Equal(0, _http.CdnRequests);
        Assert.Equal(0, _http.ModelCalls);
    }

    // Search needs Indexed, and the row has an annotation now. The attempt budget belongs to the
    // API path: the import spends none of it.
    [Theory]
    [InlineData(MemeIndexStatus.Pending)]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Skipped)]
    public async Task ImportAsync_ExistingRowThatIsNotIndexed_BecomesIndexedAndKeepsItsAttemptCount(MemeIndexStatus status)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        var seeded = SeedRow(1001UL, 11UL, status, attemptCount: 2);
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1)));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Imported, result.Outcome);
        Assert.False(result.Overwritten);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(seeded.Id, row.Id);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Null(row.Error);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(row.Id, (await verify.MemeAnnotations.SingleAsync()).MemeIndexId);
    }

    // The key is the identity, whoever wrote it first: an import under the bot's own
    // (model, prompt version) replaces the annotation the API path stored.
    [Fact]
    public async Task ImportAsync_KeyAlreadyWrittenByTheApiPath_OverwritesThatAnnotation()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        await RunJobAsync(sweep: false);
        MemeAnnotationEntity apiAnnotation;
        MemeIndexEntity apiRow;
        await using (var read = NewContext())
        {
            apiAnnotation = await read.MemeAnnotations.SingleAsync();
            apiRow = await read.MemeIndex.SingleAsync();
        }
        Assert.Equal((ConfiguredModel, "Opis obrazka 1"), (apiAnnotation.ModelId, apiAnnotation.DescriptionPl));
        var replacement = OtherMetadata();

        var response = await ImportAsync(Item(11UL, replacement, modelId: ConfiguredModel));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Imported, result.Outcome);
        Assert.True(result.Overwritten);
        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var annotation = await verify.MemeAnnotations.SingleAsync();
        Assert.Equal(apiAnnotation.Id, annotation.Id);
        Assert.Equal(apiAnnotation.FirstSeenUtc, annotation.FirstSeenUtc);
        AssertOtherMetadataStored(annotation);
        StoredMemeOutput.AssertVerbatimRawResponse(annotation, replacement.ToJsonString());
        // The lifecycle row is the API path's: its hash and its attempt stay.
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Equal(apiRow.ContentHash, row.ContentHash);
        Assert.Equal(apiRow.AttemptCount, row.AttemptCount);
    }

    // A stored `people` value outside the contract (a row no writer of today can produce) must
    // not kill the batch at the comparison: it counts as different, and the overwrite repairs it.
    [Fact]
    public async Task ImportAsync_StoredPeopleOutsideTheContract_IsOverwrittenAndTheBatchGoesOn()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        await _db.SaveChangesAsync();
        var row = SeedRow(1001UL, 11UL, MemeIndexStatus.Indexed, attemptCount: 1);
        row.Annotations.Add(new MemeAnnotationEntity
        {
            AttachmentDiscordId = 11UL,
            ModelId = ImportModel,
            PromptVersion = OpenRouterClient.PromptVersion,
            IndexedAtUtc = DateTime.UtcNow,
            DescriptionPl = "Opis obrazka 1",
            DescriptionEn = "Description of image 1",
            OcrText = "",
            Tags = ["test", "mem"],
            People = """[{"name":"Adam Małysz","evidence":"guessed"}]""",
        });
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1)), Item(12UL, Metadata(2)));

        Assert.All(response.Items, i => Assert.Equal(MemeAnnotationImportOutcome.Imported, i.Outcome));
        Assert.Equal([true, false], response.Items.Select(i => i.Overwritten));
        await using var verify = NewContext();
        StoredMemeOutput.AssertDefaultOutput(await verify.MemeAnnotations.SingleAsync(a => a.AttachmentDiscordId == 11UL));
        Assert.Equal(2, await verify.MemeAnnotations.CountAsync());
    }

    // An annotation under another version would not be what its key says it is. The match is exact.
    [Theory]
    [InlineData("legacy")]
    [InlineData("v2")]
    [InlineData("V4")]
    [InlineData(" v4")]
    [InlineData("")]
    public async Task ImportAsync_UnknownPromptVersion_IsRejectedAndWritesNothing(string promptVersion)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1), promptVersion: promptVersion));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Rejected, result.Outcome);
        Assert.Equal("11", result.AttachmentDiscordId);
        Assert.Contains("prompt_version", result.Reason);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public void KnownPromptVersions_HoldsTheCurrentPromptVersion()
    {
        Assert.Contains(OpenRouterClient.PromptVersion, OpenRouterClient.KnownPromptVersions);
    }

    // model_id is a part of the key: "x " next to "x" would be a second annotation of one writer.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" " + ImportModel)]
    [InlineData(ImportModel + " ")]
    [InlineData(ImportModel + "\n")]
    [InlineData("\t" + ImportModel)]
    public async Task ImportAsync_ModelIdBlankOrWithOuterWhitespace_IsRejectedAndWritesNothing(string modelId)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1), modelId: modelId));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Rejected, result.Outcome);
        Assert.Contains("model_id", result.Reason);
        await AssertNothingStoredAsync();
    }

    // Opus output from the subscription run has no provider-enforced schema: such items are
    // expected, and each one is reported by itself. hasAttachmentId = the item got far enough
    // to be read, so the result can name the attachment.
    [Theory]
    [InlineData("source outside the closed set", true)]
    [InlineData("source that is not a string", true)]
    [InlineData("image_kind outside the closed set", true)]
    [InlineData("image_kind as a comma list", true)]
    [InlineData("image_kind in another case", true)]
    [InlineData("image_kind as a number", true)]
    [InlineData("language outside the closed set", true)]
    [InlineData("evidence outside the closed set", true)]
    [InlineData("required member missing", true)]
    [InlineData("nullable member missing", true)]
    [InlineData("required member explicit null", true)]
    [InlineData("image_kind explicit null", true)]
    [InlineData("null inside tags", true)]
    [InlineData("null inside people", true)]
    [InlineData("person without a name", true)]
    [InlineData("metadata is a string", true)]
    [InlineData("metadata is an array", true)]
    [InlineData("metadata is null", true)]
    [InlineData("metadata is missing", false)]
    [InlineData("item is a number", false)]
    [InlineData("item is null", false)]
    [InlineData("item is an array", false)]
    [InlineData("attachment id is missing", false)]
    [InlineData("attachment id is not a number", false)]
    [InlineData("attachment id is negative", false)]
    [InlineData("indexed_at_utc is not a date", false)]
    public async Task ImportAsync_InvalidItemBetweenTwoValidOnes_IsRejectedAndTheOthersAreStored(
        string violation, bool hasAttachmentId)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"), Attachment(13UL, "c.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(
            Item(11UL, Metadata(1)),
            InvalidItem(12UL, violation),
            Item(13UL, Metadata(3)));

        Assert.Equal([0, 1, 2], response.Items.Select(i => i.Index));
        Assert.Equal(
            [MemeAnnotationImportOutcome.Imported, MemeAnnotationImportOutcome.Rejected, MemeAnnotationImportOutcome.Imported],
            response.Items.Select(i => i.Outcome));
        Assert.Equal(["11", hasAttachmentId ? "12" : null, "13"], response.Items.Select(i => i.AttachmentDiscordId));
        Assert.False(string.IsNullOrWhiteSpace(response.Items[1].Reason));
        Assert.Equal((2, 0, 0, 1), Counts(response));

        await using var verify = NewContext();
        Assert.Equal([11UL, 13UL],
            await verify.MemeAnnotations.OrderBy(a => a.AttachmentDiscordId).Select(a => a.AttachmentDiscordId).ToListAsync());
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 13UL], rows.Select(r => r.AttachmentDiscordId));
        Assert.All(rows, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));
    }

    // Two answers for one key in one file: the import cannot know which one is meant, and the
    // second must not silently replace the first.
    [Fact]
    public async Task ImportAsync_SameKeyTwiceInOneBatch_ImportsTheFirstAndRejectsTheSecond()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1)), Item(11UL, OtherMetadata()));

        Assert.Equal([MemeAnnotationImportOutcome.Imported, MemeAnnotationImportOutcome.Rejected],
            response.Items.Select(i => i.Outcome));
        Assert.Contains("duplicate", response.Items[1].Reason);
        Assert.Equal((1, 0, 0, 1), Counts(response));
        await using var verify = NewContext();
        Assert.Equal("Opis obrazka 1", (await verify.MemeAnnotations.SingleAsync()).DescriptionPl);
    }

    // N annotations per attachment (#367): another model is another key, also inside one batch,
    // where the lifecycle row of the first item is the row of the second.
    [Fact]
    public async Task ImportAsync_TwoModelsForOneAttachmentInOneBatch_StoresBothUnderOneLifecycleRow()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(
            Item(11UL, Metadata(1), modelId: "model/a"),
            Item(11UL, OtherMetadata(), modelId: "model/b"));

        Assert.All(response.Items, i => Assert.Equal(MemeAnnotationImportOutcome.Imported, i.Outcome));
        Assert.Equal((2, 0, 0, 0), Counts(response));
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        var annotations = await verify.MemeAnnotations.OrderBy(a => a.ModelId).ToListAsync();
        Assert.Equal(["model/a", "model/b"], annotations.Select(a => a.ModelId));
        Assert.All(annotations, a => Assert.Equal(row.Id, a.MemeIndexId));
    }

    // A snowflake is above 2^53: jq and JavaScript round it as a number, so an export writes a
    // string. A number is what the bot's own attachments_json holds. Both are read; the result
    // always carries a string.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportAsync_AttachmentIdAsStringOrNumber_IsReadAndReportedAsAString(bool asString)
    {
        const ulong Snowflake = 1507736402232741899UL;
        AddMessage(1001UL, _channel, Attachment(Snowflake, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(Snowflake, Metadata(1), idAsString: asString));

        var result = Assert.Single(response.Items);
        Assert.Equal(MemeAnnotationImportOutcome.Imported, result.Outcome);
        Assert.Equal("1507736402232741899", result.AttachmentDiscordId);
        await using var verify = NewContext();
        Assert.Equal(Snowflake, (await verify.MemeAnnotations.SingleAsync()).AttachmentDiscordId);
        Assert.Equal(Snowflake, (await verify.MemeIndex.SingleAsync()).AttachmentDiscordId);
    }

    // timestamptz takes UTC only. No offset = UTC (the field name says so); an offset is converted.
    [Theory]
    [InlineData("2026-09-01T12:00:00Z", 12)]
    [InlineData("2026-09-01T12:00:00", 12)]
    [InlineData("2026-09-01T12:00:00+02:00", 10)]
    [InlineData("2026-09-01T12:00:00-05:00", 17)]
    public async Task ImportAsync_IndexedAtUtc_IsStoredAsThatInstantInUtc(string indexedAtUtc, int storedHourUtc)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1), indexedAtUtc: indexedAtUtc));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        Assert.Equal(new DateTime(2026, 9, 1, storedHourUtc, 0, 0, DateTimeKind.Utc),
            (await verify.MemeAnnotations.SingleAsync()).IndexedAtUtc);
    }

    [Theory]
    [InlineData(false)] // the member is left out
    [InlineData(true)]  // the member is an explicit null
    public async Task ImportAsync_NoIndexedAtUtc_StoresTheTimeOfTheImport(bool explicitNull)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        var item = Item(11UL, Metadata(1));
        if (explicitNull)
            item["indexed_at_utc"] = null;
        var startedAtUtc = DateTime.UtcNow;

        var response = await ImportAsync(item);

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        Assert.InRange((await verify.MemeAnnotations.SingleAsync()).IndexedAtUtc,
            startedAtUtc.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    // Provenance on the annotation, like the API path: absent and blank both mean the model's
    // own default, stored as NULL.
    [Theory]
    [InlineData("low", "low")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public async Task ImportAsync_ReasoningEffort_IsStoredAndBlankBecomesNull(string? reasoningEffort, string? storedEffort)
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();

        var response = await ImportAsync(Item(11UL, Metadata(1), reasoningEffort: reasoningEffort));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        Assert.Equal(storedEffort, (await verify.MemeAnnotations.SingleAsync()).ReasoningEffort);
    }

    // The point of the whole ticket: an imported annotation is what search answers with.
    [Fact]
    public async Task ImportAsync_ImportedMeme_IsFoundBySearch()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        await using (var before = NewContext())
            Assert.Empty(await SearchDrakeAsync(before));

        await ImportAsync(Item(11UL, Metadata(1)));

        await using var db = NewContext();
        var hit = Assert.Single(await SearchDrakeAsync(db));
        Assert.Equal((ChannelDiscordId, 1001UL, 11UL), (hit.ChannelDiscordId, hit.MessageDiscordId, hit.AttachmentDiscordId));
        Assert.Equal("Opis obrazka 1", hit.DescriptionPl);
    }

    // The status row is the gate of search: a Failed row with an imported annotation would stay hidden.
    [Fact]
    public async Task ImportAsync_ImportOverAFailedRow_MakesTheMemeFindable()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        SeedRow(1001UL, 11UL, MemeIndexStatus.Failed, attemptCount: 3);
        await _db.SaveChangesAsync();

        await ImportAsync(Item(11UL, Metadata(1)));

        await using var db = NewContext();
        var hit = Assert.Single(await SearchDrakeAsync(db));
        Assert.Equal(11UL, hit.AttachmentDiscordId);
    }

    // #373: an image the API model refused is not lost. The import adds an annotation, and search finds it.
    [Fact]
    public async Task ImportAsync_ImportOverARowTheApiModelRefused_MakesTheMemeFindable()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.RefusalFor.Add(Png(1));
        await RunJobAsync(sweep: false);
        await using (var refused = NewContext())
        {
            var row = await refused.MemeIndex.SingleAsync();
            Assert.Equal((MemeIndexStatus.Skipped, ConfiguredModel), (row.Status, row.RefusedByModelId));
        }

        var response = await ImportAsync(Item(11UL, Metadata(1)));

        Assert.Equal(MemeAnnotationImportOutcome.Imported, Assert.Single(response.Items).Outcome);
        await using var db = NewContext();
        var indexed = await db.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, indexed.Status);
        Assert.Null(indexed.Error);
        // The marker stays: the manual backfill must not offer the image to the refusing model again.
        Assert.Equal(ConfiguredModel, indexed.RefusedByModelId);
        Assert.Equal(ImportModel, (await db.MemeAnnotations.SingleAsync()).ModelId);
        var hit = Assert.Single(await SearchDrakeAsync(db));
        Assert.Equal(11UL, hit.AttachmentDiscordId);

        await RunJobAsync(sweep: false);
        Assert.Equal(1, _http.ModelCalls);
    }

    // The sweep looks at the status only: it never pays for an imported attachment.
    [Fact]
    public async Task ExecuteSweepAsync_AfterImport_MakesNoDownloadAndNoModelCall()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        await ImportAsync(Item(11UL, OtherMetadata()));

        await RunJobAsync(sweep: true);

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        Assert.Equal(ImportModel, (await verify.MemeAnnotations.SingleAsync()).ModelId);
    }

    // The control for the test above: the same sweep does pay for an attachment nobody imported.
    [Fact]
    public async Task ExecuteSweepAsync_AfterImport_StillIndexesTheAttachmentThatWasNotImported()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "imported.png"), Attachment(12UL, "not-imported.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        await ImportAsync(Item(11UL, OtherMetadata()));

        await RunJobAsync(sweep: true);

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        Assert.Equal([(11UL, ImportModel), (12UL, ConfiguredModel)],
            (await verify.MemeAnnotations.OrderBy(a => a.AttachmentDiscordId).ToListAsync())
                .Select(a => (a.AttachmentDiscordId, a.ModelId)));
    }

    // The one way the API model reaches an imported meme, and a human starts it. It also fills
    // the hash the import could not have.
    [Fact]
    public async Task ExecuteAsync_AfterImport_AddsTheConfiguredModelsAnnotationAndFillsTheContentHash()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        await ImportAsync(Item(11UL, OtherMetadata()));

        await RunJobAsync(sweep: false);

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.NotNull(row.ContentHash);
        var annotations = await verify.MemeAnnotations.OrderBy(a => a.ModelId).ToListAsync();
        Assert.Equal([ImportModel, ConfiguredModel], annotations.Select(a => a.ModelId));
        // The import's annotation is not touched by the API run.
        AssertOtherMetadataStored(annotations[0]);
        Assert.Equal("Opis obrazka 1", annotations[1].DescriptionPl);
    }

    // The import of the configured key is the configured writer's annotation: the manual backfill
    // has nothing left to pay for.
    [Fact]
    public async Task ExecuteAsync_AfterImportUnderTheConfiguredKey_MakesNoModelCall()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        await ImportAsync(Item(11UL, OtherMetadata(), modelId: ConfiguredModel));

        await RunJobAsync(sweep: false);

        Assert.Equal(0, _http.ModelCalls);
        Assert.Equal(0, _http.CdnRequests);
        await using var verify = NewContext();
        AssertOtherMetadataStored(await verify.MemeAnnotations.SingleAsync());
    }

    // Valid JSON and a valid MemeMetadata, but Postgres refuses U+0000 in text and in jsonb, so
    // the save throws (the #311 poison row). One refused item must not take the batch with it:
    // the failed entities leave the context, and the next item saves.
    [Fact]
    public async Task ImportAsync_ItemTheDatabaseRefuses_IsRejectedAndTheRestOfTheBatchIsStored()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"), Attachment(13UL, "c.png"));
        await _db.SaveChangesAsync();
        var poison = Metadata(2);
        poison["ocr_text"] = "top text \0 bottom text";

        var response = await ImportAsync(Item(11UL, Metadata(1)), Item(12UL, poison), Item(13UL, Metadata(3)));

        Assert.Equal(
            [MemeAnnotationImportOutcome.Imported, MemeAnnotationImportOutcome.Rejected, MemeAnnotationImportOutcome.Imported],
            response.Items.Select(i => i.Outcome));
        Assert.Equal("12", response.Items[1].AttachmentDiscordId);
        Assert.StartsWith("save failed", response.Items[1].Reason);
        Assert.Equal((2, 0, 0, 1), Counts(response));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("refused by the database"));

        await using var verify = NewContext();
        Assert.Equal([11UL, 13UL],
            await verify.MemeAnnotations.OrderBy(a => a.AttachmentDiscordId).Select(a => a.AttachmentDiscordId).ToListAsync());
        // The lifecycle row of the refused item went with its annotation: no Indexed row without one.
        Assert.Equal([11UL, 13UL],
            await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).Select(m => m.AttachmentDiscordId).ToListAsync());
    }

    // A refused item wrote nothing, so its key is free again: a corrected item for the same key
    // later in the batch is not a duplicate.
    [Fact]
    public async Task ImportAsync_CorrectedItemAfterARefusedOneWithTheSameKey_IsImported()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        var poison = Metadata(1);
        poison["ocr_text"] = "top text \0 bottom text";

        var response = await ImportAsync(Item(11UL, poison), Item(11UL, Metadata(1)));

        Assert.Equal([MemeAnnotationImportOutcome.Rejected, MemeAnnotationImportOutcome.Imported],
            response.Items.Select(i => i.Outcome));
        Assert.False(response.Items[1].Overwritten);
        await using var verify = NewContext();
        Assert.Equal("", (await verify.MemeAnnotations.SingleAsync()).OcrText);
        Assert.Equal(MemeIndexStatus.Indexed, (await verify.MemeIndex.SingleAsync()).Status);
    }

    // The same refusal over a row that exists already: the status flip is part of the refused
    // save, so the row stays what it was.
    [Fact]
    public async Task ImportAsync_ItemTheDatabaseRefuses_LeavesAnExistingRowAsItWas()
    {
        AddMessage(1001UL, _channel, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        SeedRow(1001UL, 11UL, MemeIndexStatus.Failed, attemptCount: 2);
        await _db.SaveChangesAsync();
        var poison = Metadata(1);
        poison["ocr_text"] = "top text \0 bottom text";

        var response = await ImportAsync(Item(11UL, poison));

        Assert.Equal(MemeAnnotationImportOutcome.Rejected, Assert.Single(response.Items).Outcome);
        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync();
        Assert.Equal(MemeIndexStatus.Failed, row.Status);
        Assert.Equal("seeded by test", row.Error);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_NoItems_ReturnsAnEmptyResultAndWritesNothing()
    {
        var response = await ImportAsync();

        Assert.Empty(response.Items);
        Assert.Equal((0, 0, 0, 0), Counts(response));
        await AssertNothingStoredAsync();
    }

    private static (int Imported, int Overwritten, int Skipped, int Rejected) Counts(MemeAnnotationImportResponse response) =>
        (response.Imported, response.Overwritten, response.Skipped, response.Rejected);

    private async Task AssertNothingStoredAsync()
    {
        await using var verify = NewContext();
        Assert.Equal(0, await verify.MemeAnnotations.CountAsync());
        Assert.Equal(0, await verify.MemeIndex.CountAsync());
    }

    // The request body as the endpoint hands it over: the elements of one JSON array.
    private async Task<MemeAnnotationImportResponse> ImportAsync(params JsonNode?[] items)
    {
        using var body = JsonDocument.Parse(new JsonArray(items).ToJsonString());

        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MemeAnnotationImportService>()
            .ImportAsync([.. body.RootElement.EnumerateArray()], CancellationToken.None);
    }

    private async Task RunJobAsync(bool sweep)
    {
        // The sweep runs with the switch on: with it off it would make no call for any attachment.
        await using var provider = BuildProvider(automaticIndexing: sweep);
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<MemeIndexingJob>();
        if (sweep)
            await job.ExecuteSweepAsync(GuildDiscordId, CancellationToken.None);
        else
            await job.ExecuteAsync(GuildDiscordId, CancellationToken.None);
    }

    // The registrations of Program.cs that the import and the indexing job need. AutomaticIndexing
    // is off by default, as in production at import time: the import does not read it.
    private ServiceProvider BuildProvider(bool automaticIndexing = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // A closed registration wins over AddLogging's open ILogger<>.
        services.AddSingleton(_log.For<MemeAttachmentIndexer>());
        services.AddSingleton(_log.For<MemeAnnotationImportService>());
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = [ChannelDiscordId];
            o.AutomaticIndexing = automaticIndexing;
        });
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = ConfiguredModel;
            o.RequestDelayMs = 0;
        });
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_http));
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeAttachmentIndexer>();
        services.AddScoped<MemeAnnotationImportService>();
        services.AddScoped<BackfillJobExecutor>();
        services.AddScoped<MemeIndexingJob>();
        return services.BuildServiceProvider();
    }

    // One element of the import payload. A null reasoningEffort / indexedAtUtc leaves the member out.
    private static JsonObject Item(
        ulong attachmentId,
        JsonNode? metadata,
        string modelId = ImportModel,
        string promptVersion = OpenRouterClient.PromptVersion,
        string? indexedAtUtc = null,
        string? reasoningEffort = null,
        bool idAsString = true)
    {
        var item = new JsonObject
        {
            ["attachment_discord_id"] = idAsString ? attachmentId.ToString() : JsonValue.Create(attachmentId),
            ["model_id"] = modelId,
            ["prompt_version"] = promptVersion,
            ["metadata"] = metadata,
        };
        if (indexedAtUtc is not null)
            item["indexed_at_utc"] = indexedAtUtc;
        if (reasoningEffort is not null)
            item["reasoning_effort"] = reasoningEffort;
        return item;
    }

    // The schema v2 output FakeMemeHttpHandler would send for Png(seed): the same contract, so
    // CutoutFor / NamedPersonFor and StoredMemeOutput work for an import as they do for the API path.
    private JsonObject Metadata(byte seed) => JsonNode.Parse(_http.MetadataJsonFor(Png(seed)))!.AsObject();

    // Differs from Metadata(seed) in every member.
    private static JsonObject OtherMetadata() => new JsonObject
    {
        ["description_pl"] = "Kot siedzi na klawiaturze",
        ["description_en"] = "A cat sits on a keyboard",
        ["ocr_text"] = "DEPLOY W PIĄTEK",
        ["tags"] = new JsonArray("kot", "klawiatura"),
        ["image_kind"] = "photo_with_caption",
        ["templates"] = new JsonArray("keyboard cat"),
        ["people"] = new JsonArray(new JsonObject { ["name"] = "Robert Kubica", ["evidence"] = "name_visible" }),
        ["search_phrases"] = new JsonArray("kot programista"),
        ["franchise"] = "Shrek",
        ["source"] = "reddit",
        ["language"] = "mixed",
    };

    private static void AssertOtherMetadataStored(MemeAnnotationEntity annotation)
    {
        Assert.Equal("Kot siedzi na klawiaturze", annotation.DescriptionPl);
        Assert.Equal("A cat sits on a keyboard", annotation.DescriptionEn);
        Assert.Equal("DEPLOY W PIĄTEK", annotation.OcrText);
        Assert.Equal(["kot", "klawiatura"], annotation.Tags);
        Assert.Equal(MemeImageKind.PhotoWithCaption, annotation.ImageKind);
        Assert.Equal(["keyboard cat"], annotation.Templates);
        Assert.Equal(["Robert Kubica"], annotation.PeopleNames);
        using (var people = JsonDocument.Parse(annotation.People))
        {
            var person = Assert.Single(people.RootElement.EnumerateArray());
            Assert.Equal("Robert Kubica", person.GetProperty("name").GetString());
            Assert.Equal("name_visible", person.GetProperty("evidence").GetString());
        }
        Assert.Equal(["kot programista"], annotation.SearchPhrases);
        Assert.Equal("Shrek", annotation.Franchise);
        Assert.Equal("reddit", annotation.Source);
        Assert.Equal(MemeLanguage.Mixed, annotation.Language);
    }

    // One broken item per case. Everything the case does not name is valid, so the case is the
    // only reason for the rejection.
    private JsonNode? InvalidItem(ulong attachmentId, string violation)
    {
        var metadata = Metadata(2);
        var item = Item(attachmentId, metadata);
        switch (violation)
        {
            case "source outside the closed set":
                metadata["source"] = "Twitter";
                return item;
            case "source that is not a string":
                metadata["source"] = 7;
                return item;
            case "image_kind outside the closed set":
                metadata["image_kind"] = "meme";
                return item;
            // JsonStringEnumConverter would OR this into another member and skip the cut-out rule.
            case "image_kind as a comma list":
                metadata["image_kind"] = "cutout_face_or_emote, comic";
                return item;
            case "image_kind in another case":
                metadata["image_kind"] = "Comic";
                return item;
            case "image_kind as a number":
                metadata["image_kind"] = 2;
                return item;
            case "language outside the closed set":
                metadata["language"] = "de";
                return item;
            case "evidence outside the closed set":
                metadata["people"] = new JsonArray(new JsonObject { ["name"] = "Adam Małysz", ["evidence"] = "guessed" });
                return item;
            case "required member missing":
                metadata.Remove("tags");
                return item;
            // franchise and source may be null, but the contract still wants them present.
            case "nullable member missing":
                metadata.Remove("franchise");
                return item;
            case "required member explicit null":
                metadata["description_pl"] = null;
                return item;
            case "image_kind explicit null":
                metadata["image_kind"] = null;
                return item;
            case "null inside tags":
                metadata["tags"] = new JsonArray((JsonNode?)null);
                return item;
            case "null inside people":
                metadata["people"] = new JsonArray((JsonNode?)null);
                return item;
            case "person without a name":
                metadata["people"] = new JsonArray(new JsonObject { ["name"] = null, ["evidence"] = "name_visible" });
                return item;
            case "metadata is a string":
                item["metadata"] = "a description instead of the object";
                return item;
            case "metadata is an array":
                item["metadata"] = new JsonArray();
                return item;
            case "metadata is null":
                item["metadata"] = null;
                return item;
            case "metadata is missing":
                item.Remove("metadata");
                return item;
            case "item is a number":
                return JsonValue.Create(42);
            case "item is null":
                return null;
            case "item is an array":
                return new JsonArray();
            case "attachment id is missing":
                item.Remove("attachment_discord_id");
                return item;
            case "attachment id is not a number":
                item["attachment_discord_id"] = "twelve";
                return item;
            case "attachment id is negative":
                item["attachment_discord_id"] = -12;
                return item;
            case "indexed_at_utc is not a date":
                item["indexed_at_utc"] = "yesterday";
                return item;
            default:
                throw new ArgumentOutOfRangeException(nameof(violation), violation, "no such case");
        }
    }

    private void AddMessage(ulong discordId, ChannelEntity channel, params string[] attachments) =>
        AddMessage(discordId, channel, deleted: false, attachments);

    private void AddMessage(ulong discordId, ChannelEntity channel, bool deleted, params string[] attachments)
    {
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = deleted,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null
        });
    }

    // Call after the message is saved: the status row needs the message's database id.
    private MemeIndexEntity SeedRow(ulong messageDiscordId, ulong attachmentId, MemeIndexStatus status, int attemptCount)
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
            Status = status,
            Error = status is MemeIndexStatus.Failed or MemeIndexStatus.Skipped ? "seeded by test" : null,
            AttemptCount = attemptCount
        };
        _db.MemeIndex.Add(row);
        return row;
    }

    private static bool IsCutoutRuleEntry((LogLevel Level, string Message) entry) =>
        entry.Message.Contains("Cut-out rule applied", StringComparison.Ordinal);

    // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
    private static string Attachment(ulong id, string fileName) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{ChannelDiscordId}/{id}/{fileName}?ex=expired\",\"FileName\":\"{fileName}\",\"FileSize\":123}}";

    // Distinct valid-PNG-magic payloads (≥12 bytes for the sniffer).
    private static byte[] Png(byte seed) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, seed, seed, seed];

    // Metadata(n) carries the "drake" template: the query every findability test uses.
    private Task<List<MemeSearchHit>> SearchDrakeAsync(DiscordDbContext db) =>
        MemeSearchTestServices.NewSearch(db, fixture.ConnectionString)
            .SearchAsync(GuildDiscordId, "drake", 10, MemeSearchTestServices.AnyCaller, CancellationToken.None);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
