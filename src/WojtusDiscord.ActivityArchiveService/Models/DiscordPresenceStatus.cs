namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordPresenceStatus : BaseModel
{
    public string? Name { get; set; }
    public string? Details { get; set; }
    public DiscordStatus Status { get; set; }
    public DiscordActivityType ActivityType { get; set; }
    public DiscordActivityProperties ActivityProperties { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }
}

#region Enums

public enum DiscordStatus
{
    AFK,
    DoNotDisturb,
    Idle,
    Invisible,
    Offline,
    Online,
}

public enum DiscordActivityType
{
    Playing,
    Streaming,
    Listening,
    Watching,
    Custom,
    Competing,
}

public enum DiscordActivityProperties
{
    Embedded,
    Instance,
    Join,
    JoinRequest,
    None,
    PartyPrivacyFriends,
    PartyPrivacyVoiceChannel,
    Play,
    Spectate,
    Sync,
}

#endregion
