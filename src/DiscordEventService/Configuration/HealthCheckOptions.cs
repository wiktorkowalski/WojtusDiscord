namespace DiscordEventService.Configuration;

public class HealthCheckOptions
{
    public const string SectionName = "HealthCheck";

    public string? WebhookUrl { get; set; }
    public int FailedEventWindowMinutes { get; set; } = 5;
    public int IngestStallMinutes { get; set; } = 360;
    public int AlertCooldownMinutes { get; set; } = 30;
}
