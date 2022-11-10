namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordVoiceChannel : BaseDiscordModel
{
    public string Name { get; set; }
    public int BitRate { get; set; }
    public int UserLimit { get; set; }
    public string? RtcRegion { get; set; }

    public Guid GuildId { get; set; }
    public DiscordGuild Guild { get; set; }

    public ICollection<DiscordVoiceStatus> VoiceStatuses { get; set; }
}
