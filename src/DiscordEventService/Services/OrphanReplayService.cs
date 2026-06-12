using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public record OrphanReplayResult(int Scanned, int Inserted, int Skipped);

public class OrphanReplayService(DiscordDbContext db, ILogger<OrphanReplayService> logger)
{
    // Scan-only: JSON reconstruction is deferred until a real non-stub orphan is sampled in prod.
    public async Task<OrphanReplayResult> ReplayMemberUpdateOrphansAsync(CancellationToken ct)
    {
        var orphans = await db.RawEventLogs
            .Where(r => r.EventType == "GuildMemberUpdated"
                && !db.MemberEvents.Any(m =>
                    m.EventType == MemberEventType.Updated
                    && m.UserDiscordId == r.UserDiscordId
                    && m.ReceivedAtUtc >= r.ReceivedAtUtc.AddSeconds(-5)
                    && m.ReceivedAtUtc <= r.ReceivedAtUtc.AddSeconds(5)))
            .Select(r => new { r.Id, r.UserDiscordId, r.ReceivedAtUtc, r.SerializationFailed })
            .ToListAsync(ct);

        var skipped = 0;
        foreach (var o in orphans)
        {
            if (o.SerializationFailed)
            {
                // Unreplayable serialization stub — the original payload is gone.
                skipped++;
                continue;
            }

            // Reconstruction not yet implemented — log so we can find the sample.
            logger.LogWarning(
                "Non-stub GuildMemberUpdated orphan {RawEventLogId} for user {UserId} received at {ReceivedAtUtc}; JSON reconstruction not yet implemented",
                o.Id, o.UserDiscordId, o.ReceivedAtUtc);
            skipped++;
        }

        return new OrphanReplayResult(orphans.Count, 0, skipped);
    }
}
