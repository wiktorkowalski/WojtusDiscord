using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities
{
    public class GuildMember : BaseEntity
    {
        public string Username { get; set; }
        public string? AvatarUrl { get; set; }
        
        public User User { get; set; }
        
        public Guild Guild { get; set; }
    }
}
