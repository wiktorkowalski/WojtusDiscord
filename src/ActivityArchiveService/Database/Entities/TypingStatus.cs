using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities
{
    public class TypingStatus : BaseEntity
    {
        public Channel Channel { get; set; }

        public User User { get; set; }
    }
}
