namespace ActivityArchiveService.Database.Entities;

public class DiscordEmote : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Name { get; set; }
    public string? Url { get; set; }
    public bool IsAnimated { get; set; }

    public ICollection<DiscordReaction> Reactions { get; set; }
}