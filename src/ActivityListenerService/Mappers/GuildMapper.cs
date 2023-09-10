using ActivityListenerService.Models;
using DSharpPlus.EventArgs;

namespace ActivityListenerService.Mappers;

public static class GuildMapper
{
    public static DiscordGuild MapToDiscordGuild(this GuildCreateEventArgs createEventArgs)
    {
        return new DiscordGuild
        {
            DiscordId = createEventArgs.Guild.Id,
            Name = createEventArgs.Guild.Name,
            IconUrl = createEventArgs.Guild.IconUrl,
            DiscordTimestamp = createEventArgs.Guild.CreationTimestamp.UtcDateTime,
            InviteTimestamp = DateTime.UtcNow,
            Owner = new DiscordUser
            {
                DiscordId = createEventArgs.Guild.Owner.Id,
                Username = createEventArgs.Guild.Owner.Username,
                AvatarUrl = createEventArgs.Guild.Owner.AvatarUrl,
                DiscordTimestamp = createEventArgs.Guild.Owner.CreationTimestamp.UtcDateTime,
            },
            Emotes = createEventArgs.Guild.Emojis.Select(x => new DiscordEmotes
            {
                DiscordId = x.Value.Id,
                Name = x.Value.Name,
                IconUrl = x.Value.Url,
                DiscordTimestamp = x.Value.CreationTimestamp.UtcDateTime,
            }).ToArray(),
            Members = createEventArgs.Guild.Members.Select(x => new DiscordUser
            {
                DiscordId = x.Value.Id,
                Username = x.Value.Username,
                AvatarUrl = x.Value.AvatarUrl,
                DiscordTimestamp = x.Value.CreationTimestamp.UtcDateTime,
                
            }).ToArray(),
            Channels = createEventArgs.Guild.Channels.Select(x => new DiscordChannel
            {
                DiscordId = x.Value.Id,
                Name = x.Value.Name,
                DiscordTimestamp = x.Value.CreationTimestamp.UtcDateTime,
                Messages = Array.Empty<DiscordMessageFull>(),
            }).ToArray(),
        };
    }
}