namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordVoiceStatus : BaseModel
{
    public Guid BeforeId { get; set; }
    public DiscordVoiceStatusDetails Before { get; set; }

    public Guid AfterId { get; set; }
    public DiscordVoiceStatusDetails After { get; set; }

    public Guid VoiceChannelId { get; set; }
    public DiscordVoiceChannel VoiceChannel { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }
    
}