namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordMessageContentEdit : BaseModel
{
    public string? Content { get; set; }
    public string? ContentBefore { get; set; }
    public bool IsRemoved { get; set; }

    public DiscordMessage Message { get; set; }
}
