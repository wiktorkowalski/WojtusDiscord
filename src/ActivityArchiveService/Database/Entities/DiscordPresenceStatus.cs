namespace ActivityArchiveService.Database.Entities;

public class DiscordPresenceStatus : BaseEntity
{
    public DiscordPresenceStatusDetails Before { get; set; }

    public DiscordPresenceStatusDetails After { get; set; }

    public DiscordUser User { get; set; }
}
