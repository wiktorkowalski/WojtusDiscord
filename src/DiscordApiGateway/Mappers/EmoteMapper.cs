using DiscordApiGateway.Models;

namespace DiscordApiGateway.Mappers;

public static class EmoteMapper
{
    public static DiscordEmote MapEmote(this DSharpPlus.Entities.DiscordEmoji emote)
    {
        return new DiscordEmote
        {
            Id = emote.Id,
            Name = emote.Name,
            Url = emote.Url,
            IsAnimated = emote.IsAnimated
        };
    }
}