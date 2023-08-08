namespace ActivityArchiveService.Database.Entities
{
    public class DiscordPresenceStatusDetails : BaseEntity
    {
        public DiscordStatus DesktopStatus { get; set; }
        public DiscordStatus MobileStatus { get; set; }
        public DiscordStatus WebStatus { get; set; }

        //public DiscordPresenceStatus PresenceStatus { get; set; }
        public ICollection<DiscordActivity> Activities { get; set; }
    }

    #region Enums

    public enum DiscordStatus
    {
        Offline,// = 0,
        Online,// = 1,
        Idle,// = 2,
        DoNotDisturb,// = 4,
        Invisible,// = 5,
    }

    #endregion
}
