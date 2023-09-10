using ActivityListenerService.Models;
using DSharpPlus.EventArgs;

namespace ActivityListenerService.Mappers;

public static class VoiceMappers
{
    public static DiscordVoiceState MapToDiscordVoiceState(this VoiceStateUpdateEventArgs voiceStateUpdateEventArgs)
    {
        return new DiscordVoiceState
        {
            UserId = voiceStateUpdateEventArgs.User.Id,
            ChannelId = voiceStateUpdateEventArgs.Channel.Id,
            Before = voiceStateUpdateEventArgs.Before?.MapToDiscordVoiceStatusDetails() ?? null,
            After = voiceStateUpdateEventArgs.After?.MapToDiscordVoiceStatusDetails() ?? null,
        };
    }
    
    public static DiscordVoiceStatusDetails MapToDiscordVoiceStatusDetails(this DSharpPlus.Entities.DiscordVoiceState voiceState)
    {
        return new DiscordVoiceStatusDetails
        {
            IsSelfMuted = voiceState.IsSelfMuted,
            IsSelfDeafened = voiceState.IsSelfDeafened,
            IsSelfStream = voiceState.IsSelfStream,
            IsSelfVideo = voiceState.IsSelfVideo,
            IsServerMuted = voiceState.IsServerMuted,
            IsServerDeafened = voiceState.IsServerDeafened,
            IsSuppressed = voiceState.IsSuppressed,
        };
    }
}