using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;
using ActivityArchiveService.Database.Entities.Enums;

namespace ActivityArchiveService.Database.Entities;

public class Activity : BaseEntity
{
    public string Name { get; set; }
    public ActivityType ActivityType { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string LargeImageText { get; set; }
    public string LargeImage { get; set; }
    public string SmallImageText { get; set; }
    public string SmallImage { get; set; }
    public string? Details { get; set; }
    public string? State { get; set; }
    public string? ApplicationId { get; set; }
    public string? Party { get; set; }

    public PresenceStatusDetails Presence { get; set; }
}