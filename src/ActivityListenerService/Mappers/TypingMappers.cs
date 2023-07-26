using ActivityListenerService.Models;
using DSharpPlus.EventArgs;

namespace ActivityListenerService.Mappers;

public static class TypingMappers
{
    public static DiscordTyping MapToDiscordTyping(this TypingStartEventArgs typingStartEventArgs)
    {
        return new DiscordTyping
        {
            UserId = typingStartEventArgs.User.Id,
            ChannelId = typingStartEventArgs.Channel.Id,
        };
    }
}