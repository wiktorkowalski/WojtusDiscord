namespace DiscordApiGateway.Models;

public class DiscordEmote
{
    public ulong Id { get; set; }
    public string Name { get; set; }
    public string? Url { get; set; }
    public bool IsAnimated { get; set; }
}