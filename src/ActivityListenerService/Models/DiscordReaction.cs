namespace ActivityListenerService.Models;

public class DiscordReaction
{
    public bool IsRemoved { get; set; }

    public ulong UserId { get; set; }

    public ulong MessageId { get; set; }

    public ulong EmoteId { get; set; }
}