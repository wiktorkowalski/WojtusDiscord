using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Endpoints;

internal static class MemeIndexEndpoints
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

        // A dead job's InProgress checkpoint must not require manual DB surgery before indexing
        // can be restarted: only a live chain blocks a second start.
        var inProgress = (await MemeIndexJobEnqueuer.GetActiveGuildIdsAsync(db, CancellationToken.None)).Contains(guildId);
        if (inProgress)
            return Results.BadRequest(new { error = "Meme indexing already in progress for this guild" });

        var jobId = await MemeIndexJobEnqueuer.EnqueueAsync(db, backgroundJobClient, guildId, sweep: false, CancellationToken.None);

        return Results.Accepted("/api/ops/meme-index/status", new MemeIndexStartResponse
        {
            HangfireJobId = jobId,
            GuildId = guildId,
            Model = openRouterOptions.Value.Model,
            PromptVersion = OpenRouterClient.PromptVersion,
            ReasoningEffort = openRouterOptions.Value.ReasoningEffort,
            MaxImagesPerRun = memeIndexOptions.Value.MaxImagesPerRun,
        });
    }

    private static async Task<IResult> GetStatus(
        DiscordDbContext db, IMemeIndexSummaryReader summaryReader, CancellationToken cancellationToken)
    {
        // Through the cached reader: this endpoint has no auth, and the count scans every
        // attachment message of the meme channels. The number can be up to one minute old.
        var waiting = (int)(await summaryReader.GetAsync(cancellationToken)).Waiting;

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

        var countsByStatus = await MemeIndexStatusQueries.CountByStatusAsync(db, cancellationToken);

        var annotations = (await MemeIndexStatusQueries.CountByWriterAsync(db, cancellationToken))
            .Select(w => new MemeAnnotationCountDto
            {
                ModelId = w.ModelId,
                PromptVersion = w.PromptVersion,
                Count = w.Count,
            })
            .ToList();

        return Results.Ok(new MemeIndexStatusResponse
        {
            Checkpoints = checkpoints,
            Rows = new MemeIndexRowCounts
            {
                Pending = countsByStatus.GetValueOrDefault(MemeIndexStatus.Pending),
                Indexed = countsByStatus.GetValueOrDefault(MemeIndexStatus.Indexed),
                Failed = countsByStatus.GetValueOrDefault(MemeIndexStatus.Failed),
                Skipped = countsByStatus.GetValueOrDefault(MemeIndexStatus.Skipped),
            },
            Annotations = annotations,
            Waiting = waiting,
        });
    }
}

internal sealed record MemeIndexStartResponse
{
    public required string HangfireJobId { get; init; }
    public required ulong GuildId { get; init; }
    public required string Model { get; init; }
    public required string PromptVersion { get; init; }
    // null = no `reasoning` field is sent; the model runs at its own default effort.
    public string? ReasoningEffort { get; init; }
    public required int MaxImagesPerRun { get; init; }
}

internal sealed record MemeIndexStatusResponse
{
    public required List<MemeIndexCheckpointDto> Checkpoints { get; init; }
    public required MemeIndexRowCounts Rows { get; init; }
    // How far each writer got: one entry per (model, prompt version) that has annotations.
    public required List<MemeAnnotationCountDto> Annotations { get; init; }
    // Images in the meme channels with no meme_index row that an annotation run would take (#397).
    // Not a row count, so it is not part of Rows.
    public required int Waiting { get; init; }
}

internal sealed record MemeAnnotationCountDto
{
    public required string ModelId { get; init; }
    public required string PromptVersion { get; init; }
    public required int Count { get; init; }
}

internal sealed record MemeIndexRowCounts
{
    public int Pending { get; init; }
    public int Indexed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Total => Pending + Indexed + Failed + Skipped;
}

internal sealed record MemeIndexCheckpointDto
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
