namespace ActivityListenerService.Models;

public class DiscordPresenceStatusDetails
{
    public DiscordStatus Status { get; set; }

    public ICollection<DiscordActivity> Activities { get; set; }
}

public enum DiscordStatus
{
    Offline,// = 0,
    Online,// = 1,
    Idle,// = 2,
    DoNotDisturb,// = 4,
    Invisible,// = 5,
}