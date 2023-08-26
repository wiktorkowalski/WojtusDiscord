using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class Message : BaseDiscordEntity
{
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }
    public bool IsEdited { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime DiscordTimestamp { get; set; }

    public Message? ReplyToMessage { get; set; }

    public Channel Channel { get; set; }

    public User Author { get; set; }

    public ICollection<Reaction> Reactions { get; set; }
    public ICollection<MessageContentEdit> ContentEdits { get; set; }
}
