using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class MessageContentEdit : BaseEntity
{
    public string? Content { get; set; }
    public string? ContentBefore { get; set; }
    public bool IsRemoved { get; set; }
    
    public Message Message { get; set; }
}
