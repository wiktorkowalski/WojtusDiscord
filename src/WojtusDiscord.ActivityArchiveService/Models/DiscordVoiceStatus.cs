namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordVoiceStatus : BaseModel
{
    public Guid DetailsId { get; set; }
    public DiscordVoiceStatusDetails Details { get; set; }

    public Guid VoiceChannelId { get; set; }
    public DiscordVoiceChannel VoiceChannel { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }
    
}