using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class Channel : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Name { get; set; }
    public string? Topic { get; set; }
    public int? BitRate { get; set; }
    public int? UserLimit { get; set; }
    public string? RtcRegion { get; set; }
    public DiscordChannelType Type { get; set; }

    public Channel? ParentChannel { get; set; }

    public Guild? Guild { get; set; }

    public ICollection<Message> Messages { get; set; }
    public ICollection<VoiceStatus> VoiceStatuses { get; set; }
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