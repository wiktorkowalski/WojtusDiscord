using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Endpoints;

public static class BackfillEndpoints
{
    public static void MapBackfillEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/backfill");

        group.MapPost("/{guildId:long}", StartBackfill)
            .WithName("StartBackfill")
            .Produces<BackfillResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{guildId:long}/status", GetBackfillStatus)
            .WithName("GetBackfillStatus")
            .Produces<BackfillStatusResponse>();

        group.MapPost("/{guildId:long}/cancel", CancelBackfill)
            .WithName("CancelBackfill")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{guildId:long}/reset", ResetBackfill)
            .WithName("ResetBackfill")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> StartBackfill(
        ulong guildId,
        BackfillRequest? request,
        GuildBackfillOrchestrator orchestrator,
        DiscordDbContext db)
    {
        // Check if backfill already in progress
        var existingInProgress = await db.BackfillCheckpoints
            .AnyAsync(c => c.GuildDiscordId == guildId && c.Status == BackfillStatus.InProgress);

        if (existingInProgress)
        {
            return Results.BadRequest(new { error = "Backfill already in progress for this guild" });
        }

        var options = request?.ToOptions() ?? BackfillOptions.Default;
        var jobId = orchestrator.StartBackfill(guildId, options);

        return Results.Accepted(
            $"/api/backfill/{guildId}/status",
            new BackfillResponse { JobId = jobId, GuildId = guildId });
    }

    private static async Task<IResult> GetBackfillStatus(
        ulong guildId,
        DiscordDbContext db)
    {
        var checkpoints = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId)
            .OrderBy(c => c.Type)
            .ToListAsync();

        var overallStatus = checkpoints.Count == 0 ? "NotStarted" :
            checkpoints.Any(c => c.Status == BackfillStatus.InProgress) ? "InProgress" :
            checkpoints.Any(c => c.Status == BackfillStatus.Failed) ? "Failed" :
            checkpoints.All(c => c.Status == BackfillStatus.Completed) ? "Completed" :
            "Pending";

        return Results.Ok(new BackfillStatusResponse
        {
            GuildId = guildId,
            OverallStatus = overallStatus,
            Checkpoints = checkpoints.Select(c => new CheckpointDto
            {
                Type = c.Type.ToString(),
                Status = c.Status.ToString(),
                ProcessedCount = c.ProcessedCount,
                TotalCount = c.TotalCount,
                ErrorCount = c.ErrorCount,
                LastError = c.LastError,
                StartedAt = c.StartedAtUtc,
                CompletedAt = c.CompletedAtUtc
            }).ToList()
        });
    }

    private static async Task<IResult> CancelBackfill(
        ulong guildId,
        DiscordDbContext db,
        IBackgroundJobClient jobClient)
    {
        var inProgress = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId && c.Status == BackfillStatus.InProgress)
            .ToListAsync();

        foreach (var checkpoint in inProgress)
        {
            if (!string.IsNullOrEmpty(checkpoint.HangfireJobId))
            {
                jobClient.Delete(checkpoint.HangfireJobId);
            }
            checkpoint.Status = BackfillStatus.Cancelled;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetBackfill(
        ulong guildId,
        DiscordDbContext db)
    {
        var checkpoints = await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == guildId)
            .ToListAsync();

        db.BackfillCheckpoints.RemoveRange(checkpoints);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
}

public record BackfillRequest
{
    public bool IncludeMessages { get; init; } = true;
    public bool IncludeReactions { get; init; } = true;

    public BackfillOptions ToOptions() => new()
    {
        IncludeMessages = IncludeMessages,
        IncludeReactions = IncludeReactions
    };
}

public record BackfillResponse
{
    public required string JobId { get; init; }
    public required ulong GuildId { get; init; }
}

public record BackfillStatusResponse
{
    public required ulong GuildId { get; init; }
    public required string OverallStatus { get; init; }
    public required List<CheckpointDto> Checkpoints { get; init; }
}

public record CheckpointDto
{
    public required string Type { get; init; }
    public required string Status { get; init; }
    public int ProcessedCount { get; init; }
    public int? TotalCount { get; init; }
    public int ErrorCount { get; init; }
    public string? LastError { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
