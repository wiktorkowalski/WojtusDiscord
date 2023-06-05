namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordPresenceStatus : BaseModel
{
    public DiscordPresenceStatusDetails Before { get; set; }

    public DiscordPresenceStatusDetails After { get; set; }

    public DiscordUser User { get; set; }
}
