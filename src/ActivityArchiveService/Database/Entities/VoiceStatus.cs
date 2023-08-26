using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class VoiceStatus : BaseEntity
{
    public VoiceStatusDetails Before { get; set; }

    public VoiceStatusDetails After { get; set; }

    public Channel Channel { get; set; }

    public User User { get; set; }
    
}