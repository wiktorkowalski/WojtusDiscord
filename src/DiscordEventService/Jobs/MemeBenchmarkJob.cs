using System.Diagnostics;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Jobs;

internal sealed class MemeBenchmarkJob(
    MemeSampleService sampleService,
    AttachmentUrlRefreshService urlRefreshService,
    OpenRouterClient openRouterClient,
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> openRouterOptions,
    IOptions<MemeIndexOptions> memeIndexOptions,
    IWebHostEnvironment environment,
    ILogger<MemeBenchmarkJob> logger)
{
    public const string DownloadHttpClientName = "discord-cdn";

    public static string ReportDirectory(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "data", "meme-benchmark");

    // Links files must live here — the from-file endpoint accepts a bare file
    // name, never a path, so clients can't point the job at arbitrary files.
    public static string InputDirectory(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "data", "meme-benchmark-inputs");

    public async Task RunAsync(int sampleSize, CancellationToken cancellationToken)
    {
        var sample = await sampleService.SampleAsync(sampleSize, cancellationToken);
        if (sample.Count == 0)
        {
            logger.LogWarning("Meme benchmark found no image attachments in configured meme channels {ChannelIds}",
                string.Join(", ", memeIndexOptions.Value.ChannelIds));
            return;
        }

        await RunCoreAsync(sample, sampleSize, cancellationToken);
    }

    public async Task RunFromFileAsync(string linksFilePath, int sampleSize, CancellationToken cancellationToken)
    {
        List<MemeSampleItem>? candidates;
        await using (var stream = File.OpenRead(linksFilePath))
        {
            candidates = await JsonSerializer.DeserializeAsync<List<MemeSampleItem>>(
                stream, cancellationToken: cancellationToken);
        }

        if (candidates is null || candidates.Count == 0)
        {
            logger.LogWarning("Links file {Path} contained no items", linksFilePath);
            return;
        }

        var imageOnly = candidates.Where(c => ImageMagic.IsIndexableFileName(c.FileName)).ToList();
        var sample = MemeSampleService.Stratify(imageOnly, sampleSize);
        logger.LogInformation("Links file {Path}: {Candidates} items, {Images} indexable images, sampled {Sampled}",
            linksFilePath, candidates.Count, imageOnly.Count, sample.Count);

        await RunCoreAsync(sample, sampleSize, cancellationToken);
    }

    private async Task RunCoreAsync(List<MemeSampleItem> sample, int requestedSampleSize, CancellationToken cancellationToken)
    {
        var models = openRouterOptions.Value.BenchmarkModels;
        var startedUtc = DateTime.UtcNow;
        logger.LogInformation("Meme benchmark starting: {Count} images, models=[{Models}]",
            sample.Count, string.Join(", ", models));

        var freshUrls = await urlRefreshService.RefreshAsync(
            sample.Select(s => s.StoredUrl).ToList(), cancellationToken);

        var items = new List<BenchmarkItem>(sample.Count);
        var processed = 0;

        foreach (var sampleItem in sample)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            items.Add(await BenchmarkOneAsync(sampleItem, models, freshUrls, cancellationToken));

            if (processed % 10 == 0)
                logger.LogInformation("Meme benchmark progress: {Processed}/{Total} images", processed, sample.Count);
        }

        var run = new BenchmarkRun(startedUtc, DateTime.UtcNow, requestedSampleSize, models, items);
        var (markdownPath, jsonPath) = await WriteReportAsync(run, cancellationToken);

        var totalCost = items.SelectMany(i => i.Cells).Sum(c => c.Result.Usage?.CostUsd ?? 0);
        logger.LogInformation(
            "Meme benchmark finished: {Images} images x {Models} models, total cost {Cost:F4} USD. Report: {MarkdownPath} (raw: {JsonPath})",
            items.Count(i => i.SkipReason is null), models.Length, totalCost, markdownPath, jsonPath);
    }

    private async Task<BenchmarkItem> BenchmarkOneAsync(
        MemeSampleItem sampleItem,
        string[] models,
        AttachmentUrlRefreshResult freshUrls,
        CancellationToken cancellationToken)
    {
        // A benchmark sample is disposable either way — skip on both refresh
        // outcomes, but keep the reasons distinguishable in the report.
        switch (freshUrls.GetFreshUrl(sampleItem.StoredUrl, out var freshUrl))
        {
            case AttachmentUrlRefreshOutcome.Declined:
                return Skip(sampleItem, "URL refresh declined (attachment likely gone)");
            case AttachmentUrlRefreshOutcome.BatchFailed:
                return Skip(sampleItem, "URL refresh batch failed (transient)");
        }

        byte[] imageBytes;
        try
        {
            var client = httpClientFactory.CreateClient(DownloadHttpClientName);
            imageBytes = await client.GetByteArrayAsync(freshUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Skip(sampleItem, $"download failed: {ex.Message}");
        }

        if (imageBytes.Length > memeIndexOptions.Value.MaxImageBytes)
            return Skip(sampleItem, $"image too large ({imageBytes.Length} bytes)");

        var mimeType = ImageMagic.SniffMimeType(imageBytes);
        if (mimeType is null)
            return Skip(sampleItem, "bytes are not a recognized image format");

        var cells = new List<BenchmarkCell>(models.Length);
        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var result = await openRouterClient.AnalyzeImageAsync(imageBytes, mimeType, model, cancellationToken);
            stopwatch.Stop();

            cells.Add(new BenchmarkCell(model, result, stopwatch.Elapsed.TotalSeconds));

            if (result.Outcome == MemeAnalysisOutcome.Error)
                logger.LogWarning("Benchmark cell failed: message {MessageId}, model {Model}: {Error}",
                    sampleItem.MessageDiscordId, model, result.Error);

            await Task.Delay(TimeSpan.FromMilliseconds(openRouterOptions.Value.RequestDelayMs), cancellationToken);
        }

        return new BenchmarkItem(sampleItem, freshUrl, SkipReason: null, cells);
    }

    private BenchmarkItem Skip(MemeSampleItem sampleItem, string reason)
    {
        logger.LogInformation("Benchmark skipping message {MessageId} attachment {AttachmentId}: {Reason}",
            sampleItem.MessageDiscordId, sampleItem.AttachmentDiscordId, reason);
        return new BenchmarkItem(sampleItem, FreshUrl: null, reason, []);
    }

    private async Task<(string MarkdownPath, string JsonPath)> WriteReportAsync(
        BenchmarkRun run,
        CancellationToken cancellationToken)
    {
        var dir = ReportDirectory(environment);
        Directory.CreateDirectory(dir);

        var stamp = run.StartedUtc.ToString("yyyyMMdd-HHmmss");
        var markdownPath = Path.Combine(dir, $"benchmark-{stamp}.md");
        var jsonPath = Path.Combine(dir, $"benchmark-{stamp}.json");

        await File.WriteAllTextAsync(markdownPath, BenchmarkReportWriter.Render(run), cancellationToken);
        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        return (markdownPath, jsonPath);
    }
}
