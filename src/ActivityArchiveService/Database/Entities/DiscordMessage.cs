using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class DiscordMessage : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }
    public bool IsEdited { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime DiscordTimestamp { get; set; }

    public DiscordMessage? ReplyToMessage { get; set; }

    public DiscordChannel Channel { get; set; }

    public DiscordUser Author { get; set; }

    public ICollection<DiscordReaction> Reactions { get; set; }
    public ICollection<DiscordMessageContentEdit> ContentEdits { get; set; }
}
