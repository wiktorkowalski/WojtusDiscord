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

        var id = await tracker.OpenDowntimeAsync(
            type,
            BotDowntimeDetectionMethod.Manual,
            reason);

        return Results.Ok(new DowntimeStartResponse { Id = id, Type = type.ToString() });
    }
}

public record DowntimeStartResponse
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
}
