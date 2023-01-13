namespace WojtusDiscord.ActivityArchiveService.Models;

public class DiscordChannel : BaseDiscordModel
{
    public string Name { get; set; }
    public string? Topic { get; set; }
    public int? BitRate { get; set; }
    public int? UserLimit { get; set; }
    public string? RtcRegion { get; set; }
    public ChannelType Type { get; set; }

    public Guid? ParentChannelId { get; set; }
    public DiscordChannel? ParentChannel { get; set; }
    
    public Guid? GuildId { get; set; }
    public DiscordGuild? Guild { get; set; }

    public ICollection<DiscordMessage> Messages { get; set; }
    public ICollection<DiscordVoiceStatus> VoiceStatuses { get; set; }
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
