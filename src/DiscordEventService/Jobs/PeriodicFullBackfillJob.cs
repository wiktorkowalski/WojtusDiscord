using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

/// <summary>
/// Weekly safety-net backfill. Reconnect-backfill is capped to 2 days,
/// so anything older needs this periodic sweep. Scoped to the last 2 weeks
/// to avoid full-history crawls that take hours on large guilds.
///
/// Skips guilds with an InProgress checkpoint to avoid stepping on the
/// reconnect path or the operator-triggered /api/backfill endpoint.
/// </summary>
public class PeriodicFullBackfillJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PeriodicFullBackfillJob> logger)
{
    private static readonly TimeSpan BackfillWindow = TimeSpan.FromDays(14);

    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<GuildBackfillOrchestrator>();

        var guildIds = await db.Guilds
            .Where(g => g.LeftAtUtc == null)
            .Select(g => g.DiscordId)
            .ToListAsync();
        var inProgress = await db.BackfillCheckpoints
            .Where(c => c.Status == BackfillStatus.InProgress)
            .Select(c => c.GuildDiscordId)
            .Distinct()
            .ToListAsync();
        var inProgressSet = inProgress.ToHashSet();

        var afterTimestamp = DateTime.UtcNow - BackfillWindow;

        foreach (var guildId in guildIds)
        {
            if (inProgressSet.Contains(guildId))
            {
                logger.LogInformation(
                    "Periodic backfill skipped for guild {GuildId}: backfill already in progress",
                    guildId);
                continue;
            }

            logger.LogInformation("Periodic backfill enqueued for guild {GuildId} (window={Window}d, after={After:O})",
                guildId, BackfillWindow.TotalDays, afterTimestamp);
            orchestrator.EnqueueBackfillFrom(guildId, afterTimestamp);
        }
    }
}
