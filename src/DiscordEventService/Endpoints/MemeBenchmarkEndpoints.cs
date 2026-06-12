using DiscordEventService.Configuration;
using DiscordEventService.Jobs;
using Hangfire;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Endpoints;

public static class MemeBenchmarkEndpoints
{
    public static void MapMemeBenchmarkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ops/meme-benchmark");

        group.MapPost("/", StartBenchmark)
            .WithName("StartMemeBenchmark")
            .Produces<BenchmarkStartResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/from-file", StartBenchmarkFromFile)
            .WithName("StartMemeBenchmarkFromFile")
            .Produces<BenchmarkStartResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/report", GetLatestReport)
            .WithName("GetMemeBenchmarkReport")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static IResult StartBenchmark(
        int? sampleSize,
        IOptions<OpenRouterOptions> openRouterOptions,
        IOptions<MemeIndexOptions> memeIndexOptions,
        IBackgroundJobClient backgroundJobClient)
    {
        if (!openRouterOptions.Value.IsConfigured)
            return Results.BadRequest(new { error = "OpenRouter:ApiKey is not configured" });

        if (!memeIndexOptions.Value.IsConfigured)
            return Results.BadRequest(new { error = "MemeIndex:ChannelIds is empty — no meme channels configured" });

        var size = Math.Clamp(sampleSize ?? 100, 1, 500);
        var jobId = backgroundJobClient.Enqueue<MemeBenchmarkJob>(j => j.RunAsync(size, CancellationToken.None));

        return Results.Accepted($"/api/ops/meme-benchmark/report", new BenchmarkStartResponse
        {
            HangfireJobId = jobId,
            SampleSize = size,
            Models = openRouterOptions.Value.BenchmarkModels,
        });
    }

    // Local-run variant (#219): consumes a links file exported from the prod DB
    // (JSON array of MemeSampleItem) so the benchmark needs no prod deployment.
    // Takes a bare file name resolved under the fixed inputs directory — never
    // a client-supplied path (file-read primitive otherwise).
    private static IResult StartBenchmarkFromFile(
        string file,
        int? sampleSize,
        IOptions<OpenRouterOptions> openRouterOptions,
        IWebHostEnvironment environment,
        IBackgroundJobClient backgroundJobClient)
    {
        if (!openRouterOptions.Value.IsConfigured)
            return Results.BadRequest(new { error = "OpenRouter:ApiKey is not configured" });

        var inputRoot = Path.GetFullPath(MemeBenchmarkJob.InputDirectory(environment));
        var resolved = Path.GetFullPath(Path.Combine(inputRoot, Path.GetFileName(file)));
        if (!resolved.StartsWith(inputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(resolved))
            return Results.BadRequest(new { error = $"Links file '{Path.GetFileName(file)}' not found in {inputRoot}" });

        var size = Math.Clamp(sampleSize ?? 100, 1, 500);
        var jobId = backgroundJobClient.Enqueue<MemeBenchmarkJob>(j => j.RunFromFileAsync(resolved, size, CancellationToken.None));

        return Results.Accepted("/api/ops/meme-benchmark/report", new BenchmarkStartResponse
        {
            HangfireJobId = jobId,
            SampleSize = size,
            Models = openRouterOptions.Value.BenchmarkModels,
        });
    }

    private static async Task<IResult> GetLatestReport(
        IWebHostEnvironment environment,
        CancellationToken ct)
    {
        var dir = MemeBenchmarkJob.ReportDirectory(environment);
        if (!Directory.Exists(dir))
            return Results.NotFound(new { error = "No benchmark report yet" });

        // Stamped filenames sort chronologically, so the lexicographic max is the latest run.
        var latest = Directory.EnumerateFiles(dir, "benchmark-*.md")
            .OrderByDescending(Path.GetFileName)
            .FirstOrDefault();

        if (latest is null)
            return Results.NotFound(new { error = "No benchmark report yet" });

        var content = await File.ReadAllTextAsync(latest, ct);
        return Results.Content(content, "text/markdown; charset=utf-8");
    }
}

public sealed record BenchmarkStartResponse
{
    public required string HangfireJobId { get; init; }
    public required int SampleSize { get; init; }
    public required string[] Models { get; init; }
}
