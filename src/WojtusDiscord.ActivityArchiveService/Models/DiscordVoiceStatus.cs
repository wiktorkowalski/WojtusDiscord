namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordVoiceStatus : BaseModel
{
    public DiscordVoiceStatusDetails Before { get; set; }

    public DiscordVoiceStatusDetails After { get; set; }

    public DiscordChannel Channel { get; set; }

    public DiscordUser User { get; set; }
    
}