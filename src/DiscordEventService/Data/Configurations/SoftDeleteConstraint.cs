namespace DiscordEventService.Data.Configurations;

// Shared CHECK body keeping is_deleted <-> deleted_at_utc in sync (applied per soft-deletable entity).
// Deliberately NO global query filter (HasQueryFilter): this is an archival service — soft-deleted
// rows are first-class data for stats, timeline and the dashboard, so queries see them by default
// and filter explicitly where deletion matters (owner decision 2026-06-12).
internal static class SoftDeleteConstraint
{
    public const string Check =
        "(is_deleted = false AND deleted_at_utc IS NULL) OR " +
        "(is_deleted = true AND deleted_at_utc IS NOT NULL)";
}
