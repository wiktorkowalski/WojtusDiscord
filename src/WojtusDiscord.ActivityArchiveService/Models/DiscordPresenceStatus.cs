namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordPresenceStatus : BaseModel
{
    public DiscordPresenceStatusDetails Details { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }
}
