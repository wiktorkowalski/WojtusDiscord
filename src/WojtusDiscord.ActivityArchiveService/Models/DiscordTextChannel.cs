namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordTextChannel : BaseDiscordModel
{
    public string Name { get; set; }
    public string? Topic { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsThread { get; set; }

    public Guid? ParentTextChannelId { get; set; }
    public DiscordTextChannel? ParentTextChannel { get; set; }
    
    public Guid? GuildId { get; set; }
    public DiscordGuild? Guild { get; set; }

    public ICollection<DiscordMessage> Messages { get; set; }
}
