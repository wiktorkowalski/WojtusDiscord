namespace DiscordEventService.Data.Configurations;

// Distinct from SoftDeleteConstraint: lifecycle facts (Ban, Activity) use domain end columns
// (unbanned_at_utc / ended_at_utc), not the soft-delete convention — the shared boolean shape is coincidental.
internal static class LifecycleFactConstraint
{
    public static string Check(string endColumn) =>
        $"(is_active = false AND {endColumn} IS NOT NULL) OR " +
        $"(is_active = true AND {endColumn} IS NULL)";
}
