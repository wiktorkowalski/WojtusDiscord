using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services;

public record OrphanReplayResult(int Scanned, int Inserted, int Skipped);

public class OrphanReplayService(DiscordDbContext db, ILogger<OrphanReplayService> logger)
{
    // Scan-only for now. Reconstruction (BuildMemberUpdateEntityFromJson) is
    // deferred until a real (non-stub) GuildMemberUpdated orphan appears in
    // prod and we can sample its JSON shape — the Newtonsoft-with-[JsonProperty]
    // field names for GuildMemberUpdatedEventArgs aren't predictable without one.
    // Any such row is logged at Warn below so it can be reverse-engineered.
    //
    // Note for future reconstruction work: most non-stub orphans will NOT be
    // handler crashes. MemberEventHandler.HandleEventAsync(GuildMemberUpdatedEventArgs)
    // only inserts a MemberEventEntity when a watched field changed (nickname,
    // roles, timeout, avatar, pending, premium, mute, deafen — see lines 146-180).
    // GuildMemberUpdated for any other field (Member.Flags, UnusualDmActivityUntil,
    // etc.) writes the raw log but intentionally skips the structured insert.
    // Reconstruction must re-apply that same change-detection or it will
    // resurrect rows the handler purposefully filtered out.
    public async Task<OrphanReplayResult> ReplayMemberUpdateOrphansAsync(CancellationToken ct)
    {
        var orphans = await db.RawEventLogs
            .Where(r => r.EventType == "GuildMemberUpdated"
                && !db.MemberEvents.Any(m =>
                    m.EventType == MemberEventType.Updated
                    && m.UserDiscordId == r.UserDiscordId
                    && m.ReceivedAtUtc >= r.ReceivedAtUtc.AddSeconds(-5)
                    && m.ReceivedAtUtc <= r.ReceivedAtUtc.AddSeconds(5)))
            .Select(r => new { r.Id, r.UserDiscordId, r.ReceivedAtUtc, r.EventJson })
            .ToListAsync(ct);

        var skipped = 0;
        foreach (var o in orphans)
        {
            if (IsStubFallback(o.EventJson))
            {
                skipped++;
                continue;
            }

            // Reconstruction not yet implemented — log so we can find the sample.
            logger.LogWarning(
                "Non-stub GuildMemberUpdated orphan found: raw_event_log_id={Id} user={User} received={ReceivedAt}. JSON reconstruction not yet implemented.",
                o.Id, o.UserDiscordId, o.ReceivedAtUtc);
            skipped++;
        }

        return new OrphanReplayResult(orphans.Count, 0, skipped);
    }

    // Matches the stub written by RawEventLogService.SerializeEvent's catch path.
    // Postgres jsonb normalizes whitespace to `"key": value` (with space), so
    // we look for the spaced form. Substring match is order-independent because
    // jsonb may reorder keys on storage.
    private static bool IsStubFallback(string json)
        => json.Contains("\"error\": \"Serialization failed\"", StringComparison.Ordinal);
}
