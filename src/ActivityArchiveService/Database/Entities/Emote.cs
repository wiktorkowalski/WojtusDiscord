namespace ActivityArchiveService.Database.Entities;

public class Emote : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Name { get; set; }
    public string? Url { get; set; }
    public bool IsAnimated { get; set; }

    public ICollection<Reaction> Reactions { get; set; }
}