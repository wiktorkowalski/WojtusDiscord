using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Infrastructure;

internal static class ReactionEventQueryExtensions
{
    /// <summary>
    /// Filters to reactions that are "present" on a message: Added (live) + Backfilled
    /// (historical import). Removed/Cleared/EmojiCleared are excluded. Backfill imported
    /// ~26k of these, so filtering to Added alone under-counts reactions ~30x. (This is
    /// reactions-ever-added, not net-of-removals — fine for the stats views.)
    /// </summary>
    public static IQueryable<ReactionEventEntity> WherePresent(this IQueryable<ReactionEventEntity> reactions) =>
        reactions.Where(r =>
            r.EventType == ReactionEventType.Added || r.EventType == ReactionEventType.Backfilled);
}
