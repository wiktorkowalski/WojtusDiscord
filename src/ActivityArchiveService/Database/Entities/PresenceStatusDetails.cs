using ActivityArchiveService.Database.Entities.Base;
using ActivityArchiveService.Database.Entities.Enums;

namespace ActivityArchiveService.Database.Entities
{
    public class PresenceStatusDetails : BaseEntity
    {
        public UserStatus DesktopStatus { get; set; }
        public UserStatus MobileStatus { get; set; }
        public UserStatus WebStatus { get; set; }

        //public DiscordPresenceStatus PresenceStatus { get; set; }
        public ICollection<Activity> Activities { get; set; }
    }
}
