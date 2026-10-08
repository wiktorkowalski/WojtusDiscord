namespace DiscordEventService.Configuration;

internal sealed class HealthCheckOptions
{
    public const string SectionName = "HealthCheck";

    public string? WebhookUrl { get; set; }
    public int FailedEventWindowMinutes { get; set; } = 5;
    public int IngestStallMinutes { get; set; } = 360;
    public int AlertCooldownMinutes { get; set; } = 30;

    // Off: one burst of a type (bot edits fire MessageUpdated in bursts) lifts the mean baseline over
    // the floor, then every window with 0 events alerts until the burst leaves the baseline.
    public bool EventRatioEnabled { get; set; }

    public int EventRatioBaselineDays { get; set; } = 7;
    public int EventRatioRecentHours { get; set; } = 6;

    // NOTE: the event-ratio knobs below (drop threshold, baseline floor, debounce, exclusions)
    // are a stopgap tuned for a small/quiet server. The proper data-driven rework is tracked in #215.

    // Only near-total drops fire — a small/quiet server has too few events per
    // type for a softer ratio to be a reliable signal.
    public double EventRatioDropThreshold { get; set; } = 0.05;

    // Minimum events normally seen in the same time-of-day window before a drop
    // is worth alerting on — keeps sparse/quiet event types from tripping the alarm.
    public double EventRatioMinWindowBaseline { get; set; } = 10.0;

    // A drop must persist across this many consecutive health-check runs before it
    // alerts — debounces transient lulls on a low-traffic server.
    public int EventRatioConsecutiveRuns { get; set; } = 3;

    // Event types excluded from the ratio-drop check entirely. Voice and typing
    // traffic is naturally bursty (whole quiet days are normal — attachment-only
    // messages and bot embeds never fire TypingStarted), so both are excluded by default.
    public string[] EventRatioExcludedEventTypes { get; set; } =
        ["VoiceStateUpdated", "VoiceServerUpdated", "TypingStarted"];

    // Hours without a single event of the type before it counts as silent (#345). Sized from prod:
    // the longest natural MessageCreated gap since 2026-05-24 is 18h, while the May blackout lasted
    // 10 days with presence still flowing. The binder merges config over this default, so disable
    // a type with 0 instead of omitting it.
    public Dictionary<string, int> EventSilenceHours { get; set; } = new() { ["MessageCreated"] = 48 };

    // A live backfill saves at least once per batch, so an InProgress checkpoint untouched this long
    // is a hung or dead run. Above StaleInProgressAfter (1h) so a merely stale row has time to resume.
    public int BackfillStallHours { get; set; } = 6;

    // An open downtime row blocks every later one from opening, so one left open while the gateway
    // is connected silently stops downtime tracking.
    public int OpenDowntimeMaxMinutes { get; set; } = 60;
}
