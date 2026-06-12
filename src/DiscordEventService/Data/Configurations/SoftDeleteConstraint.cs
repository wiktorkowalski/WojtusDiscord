namespace DiscordEventService.Data.Configurations;

// Shared CHECK body keeping is_deleted <-> deleted_at_utc in sync (applied per soft-deletable entity).
internal static class SoftDeleteConstraint
{
    public const string Check =
        "(is_deleted = false AND deleted_at_utc IS NULL) OR " +
        "(is_deleted = true AND deleted_at_utc IS NOT NULL)";
}
