using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

/// <summary>
/// Weekly safety-net backfill. Reconnect-backfill is windowed by `lastAlive`,
/// so anything older than the most recent crash/reconnect can be missed by it
/// (see #124 postmortem). This job walks every known guild's full history each
/// week with `afterTimestampUtc = null` so any drift is eventually caught.
///
/// Skips guilds with an InProgress checkpoint to avoid stepping on the
/// reconnect path or the operator-triggered /api/backfill endpoint.
/// </summary>
public class PeriodicFullBackfillJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PeriodicFullBackfillJob> logger)
{
    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<GuildBackfillOrchestrator>();

        var guildIds = await db.Guilds.Select(g => g.DiscordId).ToListAsync();
        var inProgress = await db.BackfillCheckpoints
            .Where(c => c.Status == BackfillStatus.InProgress)
            .Select(c => c.GuildDiscordId)
            .Distinct()
            .ToListAsync();
        var inProgressSet = inProgress.ToHashSet();

        foreach (var guildId in guildIds)
        {
            if (inProgressSet.Contains(guildId))
            {
                logger.LogInformation(
                    "Periodic full backfill skipped for guild {GuildId}: backfill already in progress",
                    guildId);
                continue;
            }

            logger.LogInformation("Periodic full backfill enqueued for guild {GuildId}", guildId);
            orchestrator.StartBackfill(guildId);
        }
    }
}
