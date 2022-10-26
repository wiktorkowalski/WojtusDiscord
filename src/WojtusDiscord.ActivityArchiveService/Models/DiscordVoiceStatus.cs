namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordVoiceStatus : BaseModel
{
    public bool IsMuted { get; set; }
    public bool IsDeafened { get; set; }
    public bool IsSuppressed { get; set; }
    public bool IsSelfMuted { get; set; }
    public bool IsSelfDeafened { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsVideoing { get; set; }
    public string? VoiceSessionId { get; set; }
    
    public Guid VoiceChannelId { get; set; }
    public DiscordVoiceChannel VoiceChannel { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }
    
}