namespace WojtusDiscord.ActivityArchiveService.Models
{
    public class DiscordActivity : BaseModel
    {
        public string Name { get; set; }
        public DiscordActivityType ActivityType { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string LargeImageText { get; set; }
        public string LargeImage { get; set; }
        public string SmallImageText { get; set; }
        public string SmallImage { get; set; }
        public string? Details { get; set; }
        public string? State { get; set; }
        public string? ApplicationId { get; set; }
        public string? Party { get; set; }

        public Guid? EmoteId { get; set; }
        public DiscordEmote? Emote { get; set; }

        public DiscordPresenceStatusDetails Presence { get; set; }
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
}
