namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordMessage : BaseDiscordModel
{
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }

    public Guid ChannelId { get; set; }
    public DiscordTextChannel TextChannel { get; set; }

    public Guid AuthorId { get; set; }
    public DiscordUser Author { get; set; }

    public ICollection<DiscordReaction> Reactions { get; set; }
    public ICollection<DiscordMessageContentEdit> ContentEdits { get; set; }
}
