namespace ActivityListenerService.Models;

public class DiscordMessageEdit
{
    public ulong Id { get; set; }
    public string? ContentAfter { get; set; }
    public string? ContentBefore { get; set; }
    public DateTime DiscordTimestamp { get; set; }
}