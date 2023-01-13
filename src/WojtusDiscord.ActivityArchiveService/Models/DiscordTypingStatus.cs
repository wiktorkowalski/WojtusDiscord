namespace WojtusDiscord.ActivityArchiveService.Models
{
    public class DiscordTypingStatus : BaseModel
    {
        public Guid ChannelId { get; set; }
        public DiscordChannel TextChannel { get; set; }

        public Guid UserId { get; set; }
        public DiscordUser User { get; set; }
    }
}
