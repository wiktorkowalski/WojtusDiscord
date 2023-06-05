namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordEmote : BaseDiscordModel
{
    public string Name { get; set; }
    public string? Url { get; set; }
    public bool IsAnimated { get; set; }
    
    public ICollection<DiscordReaction> Reactions { get; set; }
}
