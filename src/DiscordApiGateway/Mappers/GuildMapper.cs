using DiscordApiGateway.Models;

namespace DiscordApiGateway.Mappers;

public static class GuildMapper
{
    public static DiscordGuild MapGuild(this DSharpPlus.Entities.DiscordGuild guild)
    {
        return new DiscordGuild
        {
            Name = guild.Name,
            IconUrl = guild.IconUrl,
            Owner = guild.Owner.MapUser()
        };
    } 
}