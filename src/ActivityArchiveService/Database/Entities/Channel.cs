using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;
using ActivityArchiveService.Database.Entities.Enums;

namespace ActivityArchiveService.Database.Entities;

public class Channel : BaseDiscordEntity
{
    public string Name { get; set; }
    public string? Topic { get; set; }
    public int? BitRate { get; set; }
    public int? UserLimit { get; set; }
    public string? RtcRegion { get; set; }
    public ChannelType Type { get; set; }

    public Channel? ParentChannel { get; set; }

    public Guild? Guild { get; set; }

    public ICollection<Message> Messages { get; set; }
    public ICollection<VoiceStatus> VoiceStatuses { get; set; }
}