namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordMessage : BaseDiscordModel
{
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }
    public bool IsEdited { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime DiscordTimestamp { get; set; }

    public DiscordMessage? ReplyToMessage { get; set; }

    public DiscordChannel Channel { get; set; }

    public DiscordUser Author { get; set; }

    public ICollection<DiscordReaction> Reactions { get; set; }
    public ICollection<DiscordMessageContentEdit> ContentEdits { get; set; }
}
