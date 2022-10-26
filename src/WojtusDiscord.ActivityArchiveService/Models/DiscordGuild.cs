namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordGuild : BaseDiscordModel
{
    public string Name { get; set; }
    public string IconId { get; set; }

    public Guid OwnerId { get; set; }
    public DiscordUser Owner { get; set; }

    public ICollection<DiscordTextChannel> TextChannels { get; set; }
    public ICollection<DiscordVoiceChannel> VoiceChannels { get; set; }
    public ICollection<DiscordGuildMember> Members { get; set; }
}
