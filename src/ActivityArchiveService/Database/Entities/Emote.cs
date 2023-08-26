using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class Emote : BaseDiscordEntity
{
    public string Name { get; set; }
    public string? Url { get; set; }
    public bool IsAnimated { get; set; }

    public ICollection<Reaction> Reactions { get; set; }
}