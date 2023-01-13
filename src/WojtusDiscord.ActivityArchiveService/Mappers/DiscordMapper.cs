using WojtusDiscord.ActivityArchiveService.Models;

namespace WojtusDiscord.ActivityArchiveService.Mappers;

public static class DiscordMapper
{
    public static DiscordGuild MapGuild(DSharpPlus.Entities.DiscordGuild guild, DiscordUser owner)
    {
        return new DiscordGuild
        {
            Owner = owner,
            DiscordId = guild.Id,
            Name = guild.Name,
            IconUrl = guild.IconUrl,
        };
    }

    public static DiscordUser MapUser(DSharpPlus.Entities.DiscordUser user)
    {
        return new DiscordUser
        {
            DiscordId = user.Id,
            Username = user.Username,
            Discriminator = user.Discriminator ?? "",
            AvatarUrl = user.AvatarUrl,
            IsBot = user.IsBot,
        };
    }

    public static DiscordUser MapUser(DSharpPlus.Entities.DiscordMember member)
    {
        return new DiscordUser
        {
            DiscordId = member.Id,
            Username = member.Username,
            Discriminator = member.Discriminator ?? "",
            AvatarUrl = member.AvatarUrl,
            IsBot = member.IsBot,
        };
    }

    public static DiscordUser MapWebhook(DSharpPlus.Entities.DiscordWebhook webhook)
    {
        return new DiscordUser
        {
            DiscordId = webhook.Id,
            Username = webhook.Name,
            Discriminator = webhook.Token ?? "",
            AvatarUrl = webhook.AvatarUrl,
            IsBot = false,
            IsWebhook = true,
        };
    }

    public static DiscordTextChannel MapTextChannel(DSharpPlus.Entities.DiscordChannel channel, DiscordGuild guild)
    {
        return new DiscordTextChannel
        {
            Guild = guild,
            DiscordId = channel.Id,
            Name = channel.Name,
            Topic = channel.Topic,
        };
    }

    public static DiscordTextChannel MapThread(DSharpPlus.Entities.DiscordChannel channel, DiscordTextChannel textChannel, DiscordGuild guild)
    {
        return new DiscordTextChannel
        {
            Guild = guild,
            DiscordId = channel.Id,
            Name = channel.Name,
            Topic = channel.Topic,
            ParentTextChannel = textChannel,
            IsThread = true,
        };
    }

    public static DiscordTextChannel MapDMChannel(DSharpPlus.Entities.DiscordChannel channel)
    {
        return new DiscordTextChannel
        {
            DiscordId = channel.Id,
            Name = channel.Name,
            Topic = channel.Topic,
            IsPrivate = true
        };
    }

    public static DiscordVoiceChannel MapVoiceChannel(DSharpPlus.Entities.DiscordChannel channel, DiscordGuild guild)
    {
        return new DiscordVoiceChannel
        {
            Guild = guild,
            DiscordId = channel.Id,
            Name = channel.Name,
            BitRate = channel.Bitrate ?? 0,
            UserLimit = channel.UserLimit ?? 0,
            RtcRegion = channel.RtcRegion?.Name ?? "",
        };
    }

    public static DiscordMessage MapMessage(DSharpPlus.Entities.DiscordMessage message, DiscordUser author, DiscordTextChannel channel)
    {
        return new DiscordMessage
        {
            DiscordId = message.Id,
            Content = message.Content,
            Author = author,
            TextChannel = channel,
            DiscordTimestamp = message.Timestamp.UtcDateTime
        };
    }
    
    public static DiscordReaction MapReaction(DiscordMessage message, DiscordUser user, DiscordEmote emote)
    {
        return new DiscordReaction
        {
            Message = message,
            User = user,
            Emote = emote,
        };
    }

    public static DiscordEmote MapEmote(DSharpPlus.Entities.DiscordEmoji emote)
    {
        return new DiscordEmote
        {
            DiscordId = emote.Id,
            Name = emote.Name,
            IsAnimated = emote.IsAnimated,
        };
    }

    public static DiscordVoiceStatus MapVoiceStatus(DSharpPlus.Entities.DiscordVoiceState before, DSharpPlus.Entities.DiscordVoiceState after, DiscordVoiceChannel channel, DiscordUser user)
    {
        return new DiscordVoiceStatus
        {
            User = user,
            VoiceChannel = channel,
            Before = MapVoiceStatusEntry(before),
            After = MapVoiceStatusEntry(after),
        };
    }

    public static DiscordVoiceStatusDetails MapVoiceStatusEntry(DSharpPlus.Entities.DiscordVoiceState voiceState)
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

    public static DiscordPresenceStatus MapPresenceStatus(DSharpPlus.Entities.DiscordPresence before, DSharpPlus.Entities.DiscordPresence after, DiscordUser user)
    {
        return new DiscordPresenceStatus
        {
            User = user,
            Before = MapPresenceStatusEntry(before),
            After = MapPresenceStatusEntry(after),
        };
    }

    public static DiscordPresenceStatusDetails MapPresenceStatusEntry(DSharpPlus.Entities.DiscordPresence presence)
    {
        return new DiscordPresenceStatusDetails
        {
            Name = presence.Activity.Name,
            Details = presence.Activity.RichPresence.Details,
            Status = (DiscordStatus)presence.Status,
            ActivityType = (DiscordActivityType)presence.Activity.ActivityType,
            State = presence.Activity.RichPresence?.State,
            SmallImageText = presence.Activity.RichPresence?.SmallImageText,
            LargeImageText = presence.Activity.RichPresence?.LargeImageText,
        };
    }
}
