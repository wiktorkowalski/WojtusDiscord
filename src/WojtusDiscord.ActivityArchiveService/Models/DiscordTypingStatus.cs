namespace WojtusDiscord.ActivityArchiveService.Models
{
    public class DiscordTypingStatus : BaseModel
    {
        public DiscordChannel Channel { get; set; }

        public DiscordUser User { get; set; }
    }
}
