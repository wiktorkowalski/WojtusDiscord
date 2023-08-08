using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class DiscordReaction : BaseEntity
{
    public bool IsRemoved { get; set; }

    public DiscordUser User { get; set; }
    
    public DiscordMessage Message { get; set; }

    public DiscordEmote Emote { get; set; }
}
