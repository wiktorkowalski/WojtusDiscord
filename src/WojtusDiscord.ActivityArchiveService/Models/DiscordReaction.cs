namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordReaction : BaseModel
{
    public bool IsRemoved { get; set; }

    public Guid UserId { get; set; }
    public DiscordUser User { get; set; }

    public Guid MessageId { get; set; }
    public DiscordMessage Message { get; set; }

    public Guid EmoteId { get; set; }
    public DiscordEmote Emote { get; set; }
}
