namespace DiscordEventService.Configuration;

internal sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrate { get; set; } = true;
}
