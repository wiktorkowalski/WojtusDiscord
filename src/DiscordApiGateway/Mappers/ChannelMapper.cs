using DiscordApiGateway.Models;

namespace DiscordApiGateway.Mappers;

public static class ChannelMapper
{
    public static DiscordChannel MapChannel(this DSharpPlus.Entities.DiscordChannel channel)
    {
        return new DiscordChannel
        {
            Id = channel.Id,
            Name = channel.Name,
            Topic = channel.Topic,
            BitRate = channel.Bitrate,
            UserLimit = channel.UserLimit,
            RtcRegion = channel.RtcRegion.Name,
            Type = (ChannelType)channel.Type,
            ParentChannelId = channel.Parent.Id,
            GuildId = channel.Guild.Id
        };
    }
}