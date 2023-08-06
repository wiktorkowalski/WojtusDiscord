namespace DiscordApiGateway.Models;

public class DiscordGuild
{
    public string Name { get; set; }
    public string? IconUrl { get; set; }
    
    public DiscordUser Owner { get; set; }
}