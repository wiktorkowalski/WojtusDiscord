using DiscordApiGateway.Models;

namespace DiscordApiGateway.Mappers;

public static class UserMapper
{
    public static DiscordUser MapUser(this DSharpPlus.Entities.DiscordUser user)
    {
        return new DiscordUser
        {
            Id = user.Id,
            Username = user.Username,
            Discriminator = user.Discriminator,
            AvatarUrl = user.AvatarUrl,
            IsBot = user.IsBot,
            IsWebhook = false
        };
    }
}