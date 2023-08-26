using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class Guild : BaseDiscordEntity
{
    public string Name { get; set; }
    public string? IconUrl { get; set; }

    public User Owner { get; set; }

    public ICollection<Channel> Channels { get; set; }
    public ICollection<GuildMember> Members { get; set; }
}