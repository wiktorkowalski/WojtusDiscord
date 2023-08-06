namespace DiscordApiGateway.Models;

public class DiscordMessage
{
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }
    public bool IsEdited { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime DiscordTimestamp { get; set; }

    public DiscordMessage? ReplyToMessage { get; set; }

    public ulong ChannelId { get; set; }

    public ulong AuthorId { get; set; }
}