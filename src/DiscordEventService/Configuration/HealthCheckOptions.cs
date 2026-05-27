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
    public double EventRatioDropThreshold { get; set; } = 0.1;
    public int EventRatioMinDailyBaseline { get; set; } = 10;
}
