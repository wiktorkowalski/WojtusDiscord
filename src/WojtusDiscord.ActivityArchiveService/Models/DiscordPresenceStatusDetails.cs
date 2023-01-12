namespace WojtusDiscord.ActivityArchiveService.Models
{
    public class DiscordPresenceStatusDetails : BaseModel
    {
        public string? Name { get; set; }
        public string? Details { get; set; }
        public string? State { get; set; }
        public string? LargeImageText { get; set; }
        public string? SmallImageText { get; set; }
        public DiscordStatus Status { get; set; }
        public DiscordActivityType ActivityType { get; set; }

        public DiscordPresenceStatusDetails? Before { get; set; }
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

    public enum DiscordActivityType
    {
        Playing,
        Streaming,
        ListeningTo,
        Watching,
        Custom,
        Competing,
    }

    #endregion
}
