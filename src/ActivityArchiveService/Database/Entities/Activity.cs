using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class Activity : BaseEntity
{
    public string Name { get; set; }
    public DiscordActivityType ActivityType { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
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

public enum DiscordActivityType
{
    Playing,
    Streaming,
    ListeningTo,
    Watching,
    Custom,
    Competing,
}