using ActivityListenerService.Models;
using DSharpPlus.EventArgs;

namespace ActivityListenerService.Mappers;

public static class ReactionMappers
{
    public static DiscordReaction MapToDiscordReaction(this MessageReactionAddEventArgs addEventArgs)
    {
        return new DiscordReaction
        {
            IsRemoved = false,
            UserId = addEventArgs.User.Id,
            MessageId = addEventArgs.Message.Id,
            EmoteId = addEventArgs.Emoji.Id,
        };
    }
    
    public static DiscordReaction MapToDiscordReaction(this MessageReactionRemoveEventArgs removeEventArgs)
    {
        return new DiscordReaction
        {
            IsRemoved = true,
            UserId = removeEventArgs.User.Id,
            MessageId = removeEventArgs.Message.Id,
            EmoteId = removeEventArgs.Emoji.Id,
        };
    }
}