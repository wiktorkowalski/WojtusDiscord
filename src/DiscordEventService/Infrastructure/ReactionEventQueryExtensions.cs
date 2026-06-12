using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Infrastructure;

internal static class ReactionEventQueryExtensions
{
    // Added (live) + Backfilled (historical import); Removed/Cleared/EmojiCleared excluded. Backfill imported
    // ~26k Backfilled rows, so filtering to Added alone under-counts reactions ~30x. This is reactions-ever-added,
    // not net-of-removals — fine for the stats views.
    public static IQueryable<ReactionEventEntity> WherePresent(this IQueryable<ReactionEventEntity> reactions) =>
        reactions.Where(r =>
            r.EventType == ReactionEventType.Added || r.EventType == ReactionEventType.Backfilled);
}
