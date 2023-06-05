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
            Discriminator = user.Discriminator,
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
            Discriminator = member.Discriminator,
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
            Discriminator = webhook.Token,
            AvatarUrl = webhook.AvatarUrl,
            IsBot = false,
            IsWebhook = true,
        };
    }

    public static DiscordChannel MapChannel(DSharpPlus.Entities.DiscordChannel channel, DiscordGuild guild, DiscordChannel parent = null)
    {
        return new DiscordChannel
        {
            Guild = guild,
            DiscordId = channel.Id,
            Name = channel.Name,
            Topic = channel.Topic,
            BitRate = channel.Bitrate,
            ParentChannel = parent,
            RtcRegion = channel.RtcRegion?.Name,
            Type = (ChannelType)channel.Type,
            UserLimit = channel.UserLimit
        };
    }

    public static DiscordMessage MapMessage(DSharpPlus.Entities.DiscordMessage message, DiscordUser author, DiscordChannel channel)
    {
        return new DiscordMessage
        {
            DiscordId = message.Id,
            Content = message.Content,
            Author = author,
            Channel = channel,
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

    public static DiscordVoiceStatus MapVoiceStatus(DSharpPlus.Entities.DiscordVoiceState before, DSharpPlus.Entities.DiscordVoiceState after, DiscordChannel channel, DiscordUser user)
    {
        return new DiscordVoiceStatus
        {
            User = user,
            Channel = channel,
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
            Before = MapPresenceStatusDetails(before),
            After = MapPresenceStatusDetails(after),
        };
    }

    public static DiscordPresenceStatusDetails MapPresenceStatusDetails(DSharpPlus.Entities.DiscordPresence presence)
    {
        return new DiscordPresenceStatusDetails
        {
            Activities = presence.Activities.Select(a => MapActivity(a)).ToArray(),
            DesktopStatus = (DiscordStatus)presence.ClientStatus.Desktop.Value,
            MobileStatus = (DiscordStatus)presence.ClientStatus.Mobile.Value,
            WebStatus = (DiscordStatus)presence.ClientStatus.Web.Value,
        };
    }

    public static DiscordActivity MapActivity(DSharpPlus.Entities.DiscordActivity activity)
    {
        return new DiscordActivity
        {
            Name = activity.Name,
            ActivityType = (DiscordActivityType)activity.ActivityType,
            Start = activity.RichPresence?.StartTimestamp?.UtcDateTime,
            End = activity.RichPresence?.EndTimestamp?.UtcDateTime,
            LargeImage = activity.RichPresence?.LargeImage.Id,
            LargeImageText = activity.RichPresence?.LargeImageText,
            SmallImage = activity.RichPresence?.SmallImage.Id,
            SmallImageText = activity.RichPresence?.SmallImageText,
            Details = activity.RichPresence?.Details,
            State = activity.RichPresence?.State,
            ApplicationId = activity.RichPresence?.Application.Id.ToString(),
            Party = activity.RichPresence?.PartyId.ToString(),
            Emote = activity.CustomStatus?.Emoji is null ? null : MapEmote(activity.CustomStatus.Emoji),
        };
    }
}
