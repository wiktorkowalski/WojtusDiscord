using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities
{
    public class DiscordGuildMember : BaseEntity
    {
        public string Username { get; set; }
        public string? AvatarUrl { get; set; }
        
        public DiscordUser DiscordUser { get; set; }
        
        public DiscordGuild DiscordGuild { get; set; }
    }
}
