using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class BenchmarkReportWriterTests
{
    private static readonly DateTime StartedUtc = new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

    // Complete on purpose: every member of the contract is `required`.
    private static readonly MemeMetadata Metadata = new MemeMetadata
    {
        DescriptionPl = "pl",
        DescriptionEn = "en",
        OcrText = "",
        Tags = ["tag"],
        ImageKind = MemeImageKind.TemplateMeme,
        Templates = ["drake"],
        People = [new MemePerson { Name = "Adam Małysz", Evidence = MemePersonEvidence.WidelyRecognized }],
        SearchPhrases = ["mem testowy"],
        Franchise = "Wiedźmin",
        Source = "kwejk",
        Language = MemeLanguage.Pl,
    };

    [Fact]
    public void Render_ProducesTotalsJumpLinksAndEscapedCells()
    {
        var sample = Sample(messageId: 3UL, attachmentId: 4UL, "meme.jpg", year: 2020);
        var metadata = Metadata with
        {
            DescriptionPl = "linia1\nlinia2 | z kreską",
            DescriptionEn = "desc en",
            OcrText = "ocr",
            Tags = ["kot", "cat"],
            Source = "reddit",
        };
        var ok = new BenchmarkCell("model-a", MemeAnalysisResult.Success(metadata, new MemeAnalysisUsage(100, 50, 0.01m)), 1.5);
        var failed = new BenchmarkCell("model-b", MemeAnalysisResult.Failed("HTTP 500", isTransient: true), 0.5);
        var skippedSample = Sample(messageId: 9UL, attachmentId: 10UL, "gone.png", year: 2021);

        var run = new BenchmarkRun(
            StartedUtc,
            StartedUtc.AddMinutes(10),
            RequestedSampleSize: 2,
            Slots: ["model-a", "model-b"],
            Items:
            [
                new BenchmarkItem(sample, "https://cdn.example/fresh.jpg", SkipReason: null, [ok, failed]),
                new BenchmarkItem(skippedSample, FreshUrl: null, SkipReason: "message deleted on Discord", [])
            ]);

        var markdown = BenchmarkReportWriter.Render(run);

        Assert.Contains("https://discord.com/channels/1/2/3", markdown);
        Assert.Contains("![meme](https://cdn.example/fresh.jpg)", markdown);
        Assert.Contains("| model-a | 1 | 0 | 0 | 100 | 50 | 0.0100 |", markdown);
        Assert.Contains("| model-b | 0 | 0 | 1 |", markdown);
        Assert.Contains("linia1<br>linia2 \\| z kreską", markdown);
        Assert.Contains("kot, cat", markdown);
        Assert.Contains("Error: HTTP 500", markdown);
        Assert.Contains("message deleted on Discord", markdown);
        Assert.Contains("https://discord.com/channels/1/2/9", markdown);
    }

    // One model id under two efforts is two slots: each gets its own totals row, and the
    // pipe inside the slot string must not open a new table column.
    [Fact]
    public void Render_SameModelUnderTwoEfforts_TotalsEachSlotSeparately()
    {
        const string LowSlot = "vendor/model|effort=low";
        const string HighSlot = "vendor/model|effort=high";
        var run = new BenchmarkRun(
            StartedUtc,
            StartedUtc.AddMinutes(1),
            RequestedSampleSize: 2,
            Slots: [LowSlot, HighSlot],
            Items:
            [
                Item(1UL, Success(LowSlot, costUsd: 0.01m), Success(HighSlot, costUsd: 0.05m)),
                Item(2UL, Success(LowSlot, costUsd: 0.01m), Failure(HighSlot)),
            ]);

        var markdown = BenchmarkReportWriter.Render(run);

        Assert.Contains("| vendor/model\\|effort=low | 2 | 0 | 0 | 200 | 100 | 0.0200 |", markdown);
        Assert.Contains("| vendor/model\\|effort=high | 1 | 0 | 1 | 100 | 50 | 0.0500 |", markdown);
        Assert.Contains("| field | vendor/model\\|effort=low | vendor/model\\|effort=high |", markdown);
    }

    // A slot listed twice measures run-to-run noise. Each column keeps its own totals row —
    // matched by name, both rows would count every cell of both columns.
    [Fact]
    public void Render_SameSlotListedTwice_TotalsEachColumnOnce()
    {
        var run = new BenchmarkRun(
            StartedUtc,
            StartedUtc.AddMinutes(1),
            RequestedSampleSize: 2,
            Slots: ["vendor/model", "vendor/model"],
            Items:
            [
                Item(1UL, Success("vendor/model", costUsd: 0.01m), Failure("vendor/model")),
                Item(2UL, Success("vendor/model", costUsd: 0.01m), Failure("vendor/model")),
            ]);

        var markdown = BenchmarkReportWriter.Render(run);

        Assert.Contains("| vendor/model | 2 | 0 | 0 | 200 | 100 | 0.0200 |", markdown);
        Assert.Contains("| vendor/model | 0 | 0 | 2 | 0 | 0 | 0.0000 |", markdown);
    }

    [Fact]
    public void Render_PerMemeTable_HasOneRowPerSchemaV2FieldAndNoTemplateRow()
    {
        var rows = PerMemeRows(Metadata);

        Assert.Equal(
            [
                "outcome", "description_pl", "description_en", "ocr_text", "tags", "image_kind", "templates",
                "people", "search_phrases", "franchise", "source", "language",
            ],
            rows.Select(r => r.Field));
    }

    // The report shows the contract's own words: an enum by its JSON name, not by its C# name.
    [Fact]
    public void Render_ImageKindLanguageAndPeople_UseTheJsonNames()
    {
        var metadata = Metadata with
        {
            ImageKind = MemeImageKind.CutoutFaceOrEmote,
            Language = MemeLanguage.Mixed,
            People =
            [
                new MemePerson { Name = "Adam Małysz", Evidence = MemePersonEvidence.WidelyRecognized },
                new MemePerson { Name = "Jan Nowak", Evidence = MemePersonEvidence.NameVisible },
            ],
        };

        var rows = PerMemeRows(metadata).ToDictionary(r => r.Field, r => r.Value);

        Assert.Equal("cutout_face_or_emote", rows["image_kind"]);
        Assert.Equal("mixed", rows["language"]);
        Assert.Equal("Adam Małysz (widely_recognized), Jan Nowak (name_visible)", rows["people"]);
    }

    // The benchmark has no null guard like the indexer; one null entry must not lose a paid run.
    [Fact]
    public void Render_NullPeopleElement_IsLeftOutOfThePeopleRow()
    {
        var metadata = Metadata with
        {
            People = [null!, new MemePerson { Name = "Jan Nowak", Evidence = MemePersonEvidence.NameVisible }],
        };

        var rows = PerMemeRows(metadata).ToDictionary(r => r.Field, r => r.Value);

        Assert.Equal("Jan Nowak (name_visible)", rows["people"]);
    }

    [Fact]
    public void Render_ListAndTextFields_AreJoinedOrShownAsTheyAre()
    {
        var metadata = Metadata with
        {
            Templates = ["drake", "paski tvp"],
            SearchPhrases = ["kiedy deploy w piątek", "friday deploy"],
            Franchise = "Wiedźmin",
            Source = "kwejk",
        };

        var rows = PerMemeRows(metadata).ToDictionary(r => r.Field, r => r.Value);

        Assert.Equal("drake, paski tvp", rows["templates"]);
        Assert.Equal("kiedy deploy w piątek, friday deploy", rows["search_phrases"]);
        Assert.Equal("Wiedźmin", rows["franchise"]);
        Assert.Equal("kwejk", rows["source"]);
    }

    [Fact]
    public void Render_EmptyListOrNullField_RendersAsDash()
    {
        var metadata = Metadata with { Tags = [], Templates = [], People = [], SearchPhrases = [], Franchise = null, Source = null };

        var rows = PerMemeRows(metadata).ToDictionary(r => r.Field, r => r.Value);

        Assert.All(["tags", "templates", "people", "search_phrases", "franchise", "source"], field => Assert.Equal("—", rows[field]));
    }

    // The per-meme table of a one-image, one-slot run: each row's field name and its cell.
    private static List<(string Field, string Value)> PerMemeRows(MemeMetadata metadata)
    {
        var cell = new BenchmarkCell("model-a", MemeAnalysisResult.Success(metadata, new MemeAnalysisUsage(100, 50, 0.01m)), 1.0);
        var run = new BenchmarkRun(StartedUtc, StartedUtc.AddMinutes(1), RequestedSampleSize: 1, Slots: ["model-a"], Items: [Item(1UL, cell)]);

        return
        [
            .. BenchmarkReportWriter.Render(run).Split('\n')
                .SkipWhile(line => !line.StartsWith("| field |", StringComparison.Ordinal))
                .Skip(2)
                .TakeWhile(line => line.StartsWith('|'))
                .Select(line => line.Split('|'))
                .Select(cells => (cells[1].Trim(), cells[2].Trim())),
        ];
    }

    private static BenchmarkItem Item(ulong attachmentId, params BenchmarkCell[] cells) =>
        new BenchmarkItem(Sample(messageId: attachmentId, attachmentId, "meme.png", year: 2024),
            "https://cdn.example/fresh.png", SkipReason: null, [.. cells]);

    private static BenchmarkCell Success(string slot, decimal costUsd) =>
        new BenchmarkCell(slot, MemeAnalysisResult.Success(Metadata, new MemeAnalysisUsage(100, 50, costUsd)), 1.0);

    private static BenchmarkCell Failure(string slot) =>
        new BenchmarkCell(slot, MemeAnalysisResult.Failed("HTTP 500", isTransient: true), 1.0);

    private static MemeSampleItem Sample(ulong messageId, ulong attachmentId, string fileName, int year) =>
        new MemeSampleItem(1UL, 2UL, messageId, attachmentId, fileName,
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"https://cdn.example/{fileName}?ex=old");
}
