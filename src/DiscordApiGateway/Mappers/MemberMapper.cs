using DiscordApiGateway.Models;

namespace DiscordApiGateway.Mappers;

public static class MemberMapper
{
    public static DiscordMember MapMember(this DSharpPlus.Entities.DiscordMember member)
    {
        return new DiscordMember
        {
            User = member.MapUser(),
            Guild = member.Guild.MapGuild(),
            Username = member.Username,
            AvatarUrl = member.AvatarUrl
        };
    }
}