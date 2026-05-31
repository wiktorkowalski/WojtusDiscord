namespace DiscordEventService.Configuration;

public class HealthCheckOptions
{
    public const string SectionName = "HealthCheck";

    public string? WebhookUrl { get; set; }
    public int FailedEventWindowMinutes { get; set; } = 5;
    public int IngestStallMinutes { get; set; } = 360;
    public int AlertCooldownMinutes { get; set; } = 30;
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

    // Event types excluded from the ratio-drop check entirely. Voice traffic is
    // naturally bursty (whole quiet days are normal), so it is excluded by default.
    public string[] EventRatioExcludedEventTypes { get; set; } =
        ["VoiceStateUpdated", "VoiceServerUpdated"];
}
