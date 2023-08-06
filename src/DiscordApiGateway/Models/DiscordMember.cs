namespace DiscordApiGateway.Models;

public class DiscordMember
{
    public DiscordUser User { get; set; }
    public DiscordGuild Guild { get; set; }
    
    public string Username { get; set; }
    public string? AvatarUrl { get; set; }
}