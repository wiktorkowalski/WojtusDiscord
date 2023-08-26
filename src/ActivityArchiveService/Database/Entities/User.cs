namespace ActivityArchiveService.Database.Entities;

public class User : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Username { get; set; }
    public string? Discriminator { set; get; }
    public string? AvatarUrl { get; set; }
    public bool IsBot { get; set; }
    public bool IsWebhook { get; set; }

    public ICollection<GuildMember> Guilds { get; set; }
    public ICollection<Message> Messages { get; set; }
    public ICollection<Reaction> Reactions { get; set; }
    public ICollection<VoiceStatus> VoiceStatuses { get; set; }
    public ICollection<TypingStatus> TypingStatuses { get; set; }
    public ICollection<PresenceStatus> PresenceStatuses { get; set; }
}
