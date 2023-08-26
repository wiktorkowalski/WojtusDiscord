using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityArchiveService.Database.Entities;

public class Guild : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Name { get; set; }
    public string? IconUrl { get; set; }

    public User Owner { get; set; }

    public ICollection<Channel> Channels { get; set; }
    public ICollection<GuildMember> Members { get; set; }
}