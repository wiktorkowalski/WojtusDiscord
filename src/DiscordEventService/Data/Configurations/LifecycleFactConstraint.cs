namespace DiscordEventService.Data.Configurations;

/// <summary>
/// Shared CHECK constraint body for lifecycle-fact entities (Ban, Activity), enforcing the
/// CONTEXT.md invariant <c>is_active = false ⇔ end-timestamp IS NOT NULL</c> at the DB level.
/// Distinct from <see cref="SoftDeleteConstraint"/>: lifecycle facts use domain-specific end
/// columns (<c>unbanned_at_utc</c> / <c>ended_at_utc</c>), not the soft-delete convention — the
/// shared boolean shape is a coincidence, the semantics differ.
/// </summary>
internal static class LifecycleFactConstraint
{
    public static string Check(string endColumn) =>
        $"(is_active = false AND {endColumn} IS NOT NULL) OR " +
        $"(is_active = true AND {endColumn} IS NULL)";
}
