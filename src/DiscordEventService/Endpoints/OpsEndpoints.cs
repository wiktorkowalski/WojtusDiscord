using DiscordEventService.Infrastructure.Auth;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Mvc;

namespace DiscordEventService.Endpoints;

internal static class OpsEndpoints
{
    public static void MapOpsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ops").RequireAuthorization(DashboardAuth.AdminPolicy);

        group.MapPost("/downtime/start", StartDowntimeAsync)
            .WithName("StartDowntime")
            .Produces<DowntimeStartResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/replay-orphans", ReplayOrphansAsync)
            .WithName("ReplayOrphans")
            .Produces<OrphanReplayResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/backfill-thread-channels", BackfillThreadChannelsAsync)
            .WithName("BackfillThreadChannels")
            .Produces<ThreadChannelBackfillService.Result>(StatusCodes.Status200OK);

        group.MapPost("/backfill-role-snapshots", BackfillRoleSnapshotsAsync)
            .WithName("BackfillRoleSnapshots")
            .Produces<MemberRoleSnapshotBackfillService.Result>(StatusCodes.Status200OK);

        group.MapPost("/backfill-message-mentions", BackfillMessageMentionsAsync)
            .WithName("BackfillMessageMentions")
            .Produces<MessageMentionsBackfillService.Result>(StatusCodes.Status200OK);

        group.MapPost("/failed-events/{id:guid}/resolve", ResolveFailedEventAsync)
            .WithName("ResolveFailedEvent")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // #339: scripted access to the recurring jobs; /hangfire itself is cookie-only.
        group.MapGet("/jobs", ListRecurringJobs)
            .WithName("ListRecurringJobs")
            .Produces<IEnumerable<RecurringJobResponse>>(StatusCodes.Status200OK);

        group.MapPost("/jobs/{id}/trigger", TriggerRecurringJob)
            .WithName("TriggerRecurringJob")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static IResult ListRecurringJobs(JobStorage storage)
    {
        using var connection = storage.GetConnection();
        return Results.Ok(connection.GetRecurringJobs().Select(j => new RecurringJobResponse(
            j.Id, j.Cron, j.NextExecution, j.LastExecution, j.LastJobId, j.LastJobState, j.Error)));
    }

    private static IResult TriggerRecurringJob(string id, JobStorage storage, IRecurringJobManagerV2 manager)
    {
        using (var connection = storage.GetConnection())
        {
            if (!connection.GetRecurringJobs([id]).Any(j => j.Job is not null))
                return Results.NotFound(new { error = $"No recurring job with id {id}" });
        }

        var jobId = manager.TriggerJob(id);
        return Results.Ok(new { id, jobId });
    }

    private static async Task<IResult> StartDowntimeAsync(
        BotDowntimeType type,
        string? reason,
        DowntimeTrackerService tracker)
    {
        if (!Enum.IsDefined(type))
            return Results.BadRequest(new { error = $"Unknown downtime type: {(int)type}" });

        var result = await tracker.OpenDowntimeAsync(
            type,
            BotDowntimeDetectionMethod.Manual,
            reason);

        return Results.Ok(new DowntimeStartResponse
        {
            Id = result.Id,
            Type = result.ActualType.ToString(),
            Created = result.Created,
        });
    }

    private static async Task<IResult> ReplayOrphansAsync(
        [FromQuery(Name = "event_type")] string eventType,
        OrphanReplayService svc,
        CancellationToken ct)
    {
        if (!string.Equals(eventType, "GuildMemberUpdated", StringComparison.Ordinal))
            return Results.BadRequest(new { error = $"event_type '{eventType}' not yet supported" });

        var result = await svc.ReplayMemberUpdateOrphansAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> BackfillThreadChannelsAsync(
        ThreadChannelBackfillService svc,
        CancellationToken ct)
    {
        var result = await svc.BackfillAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> BackfillRoleSnapshotsAsync(
        MemberRoleSnapshotBackfillService svc,
        CancellationToken ct)
    {
        var result = await svc.BackfillAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> BackfillMessageMentionsAsync(
        MessageMentionsBackfillService svc,
        CancellationToken ct)
    {
        var result = await svc.BackfillAsync(ct);
        return Results.Ok(result);
    }

    // Acknowledge/resolve a dead-letter row so the HealthCheckJob alert can clear by explicit
    // action. A 0-row update (unknown id or already resolved) is a clean 404, not a 500.
    private static async Task<IResult> ResolveFailedEventAsync(
        Guid id,
        string? notes,
        FailedEventService svc,
        CancellationToken ct)
    {
        var resolved = await svc.ResolveAsync(id, notes, ct);
        return resolved
            ? Results.Ok(new { id, resolved = true })
            : Results.NotFound(new { error = $"No unresolved failed event with id {id}" });
    }
}

internal sealed record RecurringJobResponse(
    string Id, string Cron, DateTime? NextExecution, DateTime? LastExecution,
    string? LastJobId, string? LastJobState, string? Error);

internal sealed record DowntimeStartResponse
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public required bool Created { get; init; }
}
