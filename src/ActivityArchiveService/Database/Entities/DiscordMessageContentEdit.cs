using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class DiscordMessageContentEdit : BaseEntity
{
    public string? Content { get; set; }
    public string? ContentBefore { get; set; }
    public bool IsRemoved { get; set; }
    
    public DiscordMessage Message { get; set; }
}
