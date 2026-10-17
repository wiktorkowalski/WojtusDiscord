using DSharpPlus.Entities;

namespace DiscordEventService.Infrastructure;

internal static class DiscordUserExtensions
{
    // The name people see in the server (#461): a member's DisplayName puts the guild nickname
    // first. A DM author or clicker is no member, so the global name comes first there.
    public static string ShownName(this DiscordUser user) =>
        user is DiscordMember member ? member.DisplayName : user.GlobalName ?? user.Username;
}
