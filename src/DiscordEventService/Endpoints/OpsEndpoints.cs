using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;

namespace DiscordEventService.Endpoints;

public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ops");

        group.MapPost("/downtime/start", StartDowntime)
            .WithName("StartDowntime")
            .Produces<DowntimeStartResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/replay-orphans", ReplayOrphans)
            .WithName("ReplayOrphans")
            .Produces<OrphanReplayResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/backfill-thread-channels", BackfillThreadChannels)
            .WithName("BackfillThreadChannels")
            .Produces<ThreadChannelBackfillService.Result>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> StartDowntime(
        BotDowntimeType type,
        string? reason,
        DowntimeTrackerService tracker)
    {
        if (!Enum.IsDefined(type))
        {
            return Results.BadRequest(new { error = $"Unknown downtime type: {(int)type}" });
        }

        var result = await tracker.OpenDowntimeAsync(
            type,
            BotDowntimeDetectionMethod.Manual,
            reason);

        return Results.Ok(new DowntimeStartResponse
        {
            Id = result.Id,
            Type = result.ActualType.ToString(),
            Created = result.Created
        });
    }

    private static async Task<IResult> ReplayOrphans(
        string event_type,
        OrphanReplayService svc,
        CancellationToken ct)
    {
        if (!string.Equals(event_type, "GuildMemberUpdated", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = $"event_type '{event_type}' not yet supported" });
        }

        var result = await svc.ReplayMemberUpdateOrphansAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> BackfillThreadChannels(
        ThreadChannelBackfillService svc,
        CancellationToken ct)
    {
        var result = await svc.BackfillAsync(ct);
        return Results.Ok(result);
    }
}

public record DowntimeStartResponse
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required bool Created { get; init; }
}
