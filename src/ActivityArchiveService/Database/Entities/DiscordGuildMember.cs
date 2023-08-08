namespace ActivityArchiveService.Database.Entities
{
    public class DiscordGuildMember : BaseEntity
    {
        public string Username { get; set; }
        public string? AvatarUrl { get; set; }
        
        public Guid DiscordUserId { get; set; }
        public DiscordUser DiscordUser { get; set; }
        
        public Guid DiscordGuildId { get; set; }
        public DiscordGuild DiscordGuild { get; set; }
    }
}
