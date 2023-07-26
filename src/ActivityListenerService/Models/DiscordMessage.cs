namespace ActivityListenerService.Models;

public class DiscordMessage
{
    public ulong Id { get; set; }
    public ulong AuthorId { get; set; }
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }
    public bool IsEdited { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime DiscordTimestamp { get; set; }
    public ulong? ReplyToMessageId { get; set; }
}