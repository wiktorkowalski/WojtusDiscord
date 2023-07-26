namespace ActivityListenerService.Models;

public class DiscordPresenceStatus
{
    public DiscordPresenceStatusDetails Before { get; set; }

    public DiscordPresenceStatusDetails After { get; set; }

    public ulong UserId { get; set; }
}