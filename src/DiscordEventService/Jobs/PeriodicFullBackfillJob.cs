using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Jobs;

// Reconnect-backfill is capped to 2 days, so anything older needs this periodic sweep;
// the orchestrator's chain guard skips guilds where a backfill is already active.
internal sealed class PeriodicFullBackfillJob(
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

        var afterTimestamp = DateTime.UtcNow - BackfillWindow;

        // The orchestrator is the single guard against overlapping chains (#289) — it skips the
        // guild (returns null) when a chain is already active and logs why.
        foreach (var guildId in guildIds)
        {
            var jobId = await orchestrator.EnqueueBackfillFromAsync(guildId, afterTimestamp);
            if (jobId is not null)
                logger.LogInformation("Periodic backfill enqueued for guild {GuildId} covering the last {WindowDays} days, after {AfterTimestampUtc:O}",
                    guildId, BackfillWindow.TotalDays, afterTimestamp);
        }
    }
}
