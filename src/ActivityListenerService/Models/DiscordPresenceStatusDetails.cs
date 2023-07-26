namespace ActivityListenerService.Models;

public class DiscordPresenceStatusDetails
{
    public DiscordStatus DesktopStatus { get; set; }
    public DiscordStatus MobileStatus { get; set; }
    public DiscordStatus WebStatus { get; set; }

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