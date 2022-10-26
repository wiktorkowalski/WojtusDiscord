namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordMessageContentEdit : BaseModel
{
    public string? Content { get; set; }
    public bool IsRemoved { get; set; }

    public Guid MessageId { get; set; }
    public DiscordMessage Message { get; set; }
}
