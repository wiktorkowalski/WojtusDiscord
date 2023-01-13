namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordPresenceStatus : BaseModel
{
    public Guid BeforeId { get; set; }
    public DiscordPresenceStatusDetails Before { get; set; }

    public Guid AfterId { get; set; }
    public DiscordPresenceStatusDetails After { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }
}
