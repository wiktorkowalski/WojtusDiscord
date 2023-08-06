namespace DiscordApiGateway.Models;

public class DiscordUser
{
    public ulong Id { get; set; }
    public string Username { get; set; }
    public string? Discriminator { set; get; }
    public string? AvatarUrl { get; set; }
    public bool IsBot { get; set; }
    public bool IsWebhook { get; set; }
}