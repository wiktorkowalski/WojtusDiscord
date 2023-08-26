using System.ComponentModel.DataAnnotations;

namespace ActivityArchiveService.Database.Entities.Base;

public class BaseEntity
{
    [Key]
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}