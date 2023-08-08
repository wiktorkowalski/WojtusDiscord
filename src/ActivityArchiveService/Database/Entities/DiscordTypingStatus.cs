namespace ActivityArchiveService.Database.Entities
{
    public class DiscordTypingStatus : BaseEntity
    {
        public DiscordChannel Channel { get; set; }

        public DiscordUser User { get; set; }
    }
}
