namespace WojtusDiscord.ActivityArchiveService.Models
{
    public class DiscordGuildMember
    {
        public Guid DiscordUserId { get; set; }
        public DiscordUser DiscordUser { get; set; }
        
        public Guid DiscordGuildId { get; set; }
        public DiscordGuild DiscordGuild { get; set; }
    }
}
