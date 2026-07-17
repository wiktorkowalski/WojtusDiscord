namespace DiscordEventService.Data.Entities.Conversations;

// Persisted enum — never renumber (follow MemeIndexStatus).
public enum UsageAlertCap
{
    GlobalMonthly = 0,
    PerUserMonthly = 1,
    GlobalDaily = 2
}

// One sent cost-cap alert (#269): the dedup ledger for soft cost alerting. A row per
// (cap, window, level[, user]) means each threshold crossing DMs the admins exactly
// once per window — restart- and redeploy-proof — and the table doubles as an audit
// trail of when spend crossed which line.
public class UsageAlertEntity
{
    public Guid Id { get; set; }

    public UsageAlertCap Cap { get; set; }

    // Calendar-UTC window this alert belongs to: first day of the month for the
    // monthly caps, the UTC date for the daily tripwire.
    public DateTime WindowStartUtc { get; set; }

    // Warn level in percent: 80 or 100 for monthly caps, 100 for the daily tripwire.
    public int Level { get; set; }

    // The over-cap user for PerUserMonthly; null for the global caps.
    public ulong? UserDiscordId { get; set; }

    // Audit trail: what the window's spend and the configured cap were at send time.
    public double SpentUsd { get; set; }
    public double LimitUsd { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
