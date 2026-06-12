using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

internal sealed record OrphanReplayResult(int Scanned, int Inserted, int Skipped);

internal sealed class OrphanReplayService(DiscordDbContext db, ILogger<OrphanReplayService> logger)
{
    // Orphan-to-member-event correlation window, +- around the orphan receive time.
    private const int OrphanMatchWindowSeconds = 5;

    // Scan-only: JSON reconstruction is deferred until a real non-stub orphan is sampled in prod.
    public async Task<OrphanReplayResult> ReplayMemberUpdateOrphansAsync(CancellationToken ct)
    {
        var orphans = await db.RawEventLogs
            .Where(r => r.EventType == "GuildMemberUpdated"
                && !db.MemberEvents.Any(m =>
                    m.EventType == MemberEventType.Updated
                    && m.UserDiscordId == r.UserDiscordId
                    && m.ReceivedAtUtc >= r.ReceivedAtUtc.AddSeconds(-OrphanMatchWindowSeconds)
                    && m.ReceivedAtUtc <= r.ReceivedAtUtc.AddSeconds(OrphanMatchWindowSeconds)))
            .Select(r => new { r.Id, r.UserDiscordId, r.ReceivedAtUtc, r.IsSerializationFailed })
            .ToListAsync(ct);

        var skipped = 0;
        foreach (var o in orphans)
        {
            if (o.IsSerializationFailed)
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
