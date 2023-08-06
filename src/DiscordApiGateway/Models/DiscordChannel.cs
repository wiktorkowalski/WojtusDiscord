namespace DiscordApiGateway.Models;

public class DiscordChannel
{
    public ulong Id { get; set; }
    public string Name { get; set; }
    public string? Topic { get; set; }
    public int? BitRate { get; set; }
    public int? UserLimit { get; set; }
    public string? RtcRegion { get; set; }
    public ChannelType Type { get; set; }

    public ulong? ParentChannelId { get; set; }
    
    public ulong GuildId { get; set; }
}

public enum ChannelType
{
    Text,
    Private,
    Voice,
    Group,
    Category,
    News,
    Store,
    NewsThread,
    PublicThread,
    PrivateThread,
    Stage,
    Unknown,
}