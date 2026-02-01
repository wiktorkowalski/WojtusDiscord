namespace DiscordEventService.Configuration;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrate { get; set; } = true;
}
