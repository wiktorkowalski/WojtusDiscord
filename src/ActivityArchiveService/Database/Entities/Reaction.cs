using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class Reaction : BaseEntity
{
    public bool IsRemoved { get; set; }

    public User User { get; set; }
    
    public Message Message { get; set; }

    public Emote Emote { get; set; }
}
