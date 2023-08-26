using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities
{
    public class GuildMember : BaseDiscordEntity
    {
        public string Username { get; set; }
        public string? AvatarUrl { get; set; }
        
        public User User { get; set; }
        
        public Guild Guild { get; set; }
    }
}
