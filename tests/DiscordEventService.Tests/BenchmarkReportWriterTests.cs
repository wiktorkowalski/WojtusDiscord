using DiscordEventService.Services.MemeIndexing;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class BenchmarkReportWriterTests
{
    [Fact]
    public void Render_ProducesTotalsJumpLinksAndEscapedCells()
    {
        var sample = new MemeSampleItem(1UL, 2UL, 3UL, 4UL, "meme.jpg", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "https://cdn.example/stored.jpg?ex=old");
        var metadata = new MemeMetadata
        {
            DescriptionPl = "linia1\nlinia2 | z kreską",
            DescriptionEn = "desc en",
            OcrText = "ocr",
            Tags = ["kot", "cat"],
            Source = "reddit",
            Template = null
        };
        var ok = new BenchmarkCell("model-a", MemeAnalysisResult.Success(metadata, new MemeAnalysisUsage(100, 50, 0.01m)), 1.5);
        var failed = new BenchmarkCell("model-b", MemeAnalysisResult.Failed("HTTP 500", isTransient: true), 0.5);
        var skippedSample = new MemeSampleItem(1UL, 2UL, 9UL, 10UL, "gone.png", new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), "https://cdn.example/gone.png?ex=old");

        var run = new BenchmarkRun(
            new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 9, 12, 10, 0, DateTimeKind.Utc),
            RequestedSampleSize: 2,
            Models: ["model-a", "model-b"],
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
}
