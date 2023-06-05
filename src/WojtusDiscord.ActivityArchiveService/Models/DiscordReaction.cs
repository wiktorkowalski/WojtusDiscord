namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordReaction : BaseModel
{
    public bool IsRemoved { get; set; }

    public DiscordUser User { get; set; }

    public DiscordMessage Message { get; set; }

    public DiscordEmote Emote { get; set; }
}
