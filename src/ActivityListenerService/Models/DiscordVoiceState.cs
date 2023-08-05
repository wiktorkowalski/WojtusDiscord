namespace ActivityListenerService.Models;

public class DiscordVoiceState
{
    public ulong UserId { get; set; }
    public ulong ChannelId { get; set; }
    public DiscordVoiceStatusDetails Before { get; set; }

    public DiscordVoiceStatusDetails After { get; set; }
}