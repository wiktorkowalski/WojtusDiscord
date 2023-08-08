namespace ActivityArchiveService.Database.Entities;

public class DiscordVoiceStatus : BaseEntity
{
    public DiscordVoiceStatusDetails Before { get; set; }

    public DiscordVoiceStatusDetails After { get; set; }

    public DiscordChannel Channel { get; set; }

    public DiscordUser User { get; set; }
    
}