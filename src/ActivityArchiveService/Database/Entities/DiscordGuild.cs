namespace ActivityArchiveService.Database.Entities;

public class DiscordGuild : BaseEntity
{
    public ulong DiscordId { get; set; }
    public string Name { get; set; }
    public string? IconUrl { get; set; }

    public DiscordUser Owner { get; set; }

    public ICollection<DiscordChannel> Channels { get; set; }
    public ICollection<DiscordGuildMember> Members { get; set; }
}