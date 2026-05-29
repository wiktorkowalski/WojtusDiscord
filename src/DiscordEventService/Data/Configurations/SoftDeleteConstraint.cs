namespace DiscordEventService.Data.Configurations;

/// <summary>
/// Shared CHECK constraint body keeping <c>is_deleted</c> ↔ <c>deleted_at_utc IS NOT NULL</c> in
/// sync (§P2.5). Applied per soft-deletable entity in its configuration.
/// </summary>
internal static class SoftDeleteConstraint
{
    public const string Check =
        "(is_deleted = false AND deleted_at_utc IS NULL) OR " +
        "(is_deleted = true AND deleted_at_utc IS NOT NULL)";
}
