using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class DiscordChannel : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Name { get; set; }
    public string? Topic { get; set; }
    public int? BitRate { get; set; }
    public int? UserLimit { get; set; }
    public string? RtcRegion { get; set; }
    public DiscordChannelType Type { get; set; }

    public DiscordChannel? ParentChannel { get; set; }

    public DiscordGuild? Guild { get; set; }

    public ICollection<DiscordMessage> Messages { get; set; }
    public ICollection<DiscordVoiceStatus> VoiceStatuses { get; set; }
}

public enum DiscordChannelType
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