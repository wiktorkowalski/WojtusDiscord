using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Endpoints;

public static class MemeIndexEndpoints
{
    public static void MapMemeIndexEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ops/meme-index");

        group.MapPost("/backfill/{guildId:long}", StartIndexing)
            .WithName("StartMemeIndexing")
            .Produces<MemeIndexStartResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/status", GetStatus)
            .WithName("GetMemeIndexStatus")
            .Produces<MemeIndexStatusResponse>();
    }

    private static async Task<IResult> StartIndexing(
        ulong guildId,
        IOptions<MemeIndexOptions> memeIndexOptions,
        IOptions<OpenRouterOptions> openRouterOptions,
        DiscordDbContext db,
        IBackgroundJobClient backgroundJobClient)
    {
        if (!memeIndexOptions.Value.IsConfigured)
            return Results.BadRequest(new { error = "MemeIndex:ChannelIds is empty — no meme channels configured" });
        if (!openRouterOptions.Value.IsConfigured)
            return Results.BadRequest(new { error = "OpenRouter:ApiKey is not configured" });
        if (string.IsNullOrWhiteSpace(openRouterOptions.Value.Model))
            return Results.BadRequest(new { error = "OpenRouter:Model is not set" });

        var inProgress = await db.BackfillCheckpoints.AnyAsync(c =>
            c.GuildDiscordId == guildId && c.Type == BackfillType.MemeIndex && c.Status == BackfillStatus.InProgress);
        if (inProgress)
            return Results.BadRequest(new { error = "Meme indexing already in progress for this guild" });

        var jobId = backgroundJobClient.Enqueue<MemeIndexingJob>(j => j.ExecuteAsync(guildId, CancellationToken.None));

        return Results.Accepted("/api/ops/meme-index/status", new MemeIndexStartResponse
        {
            HangfireJobId = jobId,
            GuildId = guildId,
            Model = openRouterOptions.Value.Model,
            MaxImagesPerRun = memeIndexOptions.Value.MaxImagesPerRun,
        });
    }

    private static async Task<IResult> GetStatus(DiscordDbContext db)
    {
        var checkpoints = await db.BackfillCheckpoints
            .Where(c => c.Type == BackfillType.MemeIndex)
            .OrderBy(c => c.GuildDiscordId)
            .Select(c => new MemeIndexCheckpointDto
            {
                GuildId = c.GuildDiscordId,
                Status = c.Status.ToString(),
                ProcessedCount = c.ProcessedCount,
                TotalCount = c.TotalCount,
                ErrorCount = c.ErrorCount,
                LastError = c.LastError,
                StartedAt = c.StartedAtUtc,
                CompletedAt = c.CompletedAtUtc,
            })
            .ToListAsync();

        var countsByStatus = await db.MemeIndex
            .GroupBy(m => m.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        return Results.Ok(new MemeIndexStatusResponse
        {
            Checkpoints = checkpoints,
            Rows = new MemeIndexRowCounts
            {
                Pending = countsByStatus.GetValueOrDefault(MemeIndexStatus.Pending),
                Indexed = countsByStatus.GetValueOrDefault(MemeIndexStatus.Indexed),
                Failed = countsByStatus.GetValueOrDefault(MemeIndexStatus.Failed),
                Skipped = countsByStatus.GetValueOrDefault(MemeIndexStatus.Skipped),
            }
        });
    }
}

public sealed record MemeIndexStartResponse
{
    public required string HangfireJobId { get; init; }
    public required ulong GuildId { get; init; }
    public required string Model { get; init; }
    public required int MaxImagesPerRun { get; init; }
}

public sealed record MemeIndexStatusResponse
{
    public required List<MemeIndexCheckpointDto> Checkpoints { get; init; }
    public required MemeIndexRowCounts Rows { get; init; }
}

public sealed record MemeIndexRowCounts
{
    public int Pending { get; init; }
    public int Indexed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Total => Pending + Indexed + Failed + Skipped;
}

public sealed record MemeIndexCheckpointDto
{
    public required ulong GuildId { get; init; }
    public required string Status { get; init; }
    public int ProcessedCount { get; init; }
    public int? TotalCount { get; init; }
    public int ErrorCount { get; init; }
    public string? LastError { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
