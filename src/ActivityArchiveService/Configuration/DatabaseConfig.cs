namespace ActivityArchiveService.Configuration;

public class DatabaseConfig
{
    public static readonly string Prefix = "Database";

    public string Host { get; init; }
    public string Port { get; init; }
    public string User { get; init; }
    public string Password { get; init; }
    public string Database { get; init; }

    public string ToConnectionString()
    {
        return $"Server={Host};Port={Port};User ID={User};Database={Database};Password={Password};";
    }
}