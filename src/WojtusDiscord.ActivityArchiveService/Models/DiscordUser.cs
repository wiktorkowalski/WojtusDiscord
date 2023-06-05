namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordUser : BaseDiscordModel
{
    public string Username { get; set; }
    public string? Discriminator { set; get; }
    public string? AvatarUrl { get; set; }
    public bool IsBot { get; set; }
    public bool IsWebhook { get; set; }

    public ICollection<DiscordGuildMember> Guilds { get; set; }
    public ICollection<DiscordMessage> Messages { get; set; }
    public ICollection<DiscordReaction> Reactions { get; set; }
    public ICollection<DiscordVoiceStatus> VoiceStatuses { get; set; }
    public ICollection<DiscordTypingStatus> TypingStatuses { get; set; }
    public ICollection<DiscordPresenceStatus> PresenceStatuses { get; set; }
}
