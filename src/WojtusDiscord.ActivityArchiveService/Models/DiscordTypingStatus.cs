namespace WojtusDiscord.ActivityArchiveService.Models
{
    public class DiscordTypingStatus : BaseModel
    {
        public Guid ChannelId { get; set; }
        public DiscordTextChannel TextChannel { get; set; }

        public Guid UserId { get; set; }
        public DiscordUser User { get; set; }
    }
}
