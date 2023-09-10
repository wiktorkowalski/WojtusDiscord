using System.Buffers;

namespace ActivityListenerService.Models;

public class DiscordGuild
{
    public string Name { get; set; }
    public ulong DiscordId { get; set; }
    public string? IconUrl { get; set; }
    public DateTime DiscordTimestamp { get; set; }
    public DateTime InviteTimestamp { get; set; }

    public DiscordUser Owner { get; set; }

    public ICollection<DiscordUser> Members { get; set; }
    
    public ICollection<DiscordChannel> Channels { get; set; }
    
    public ICollection<DiscordEmotes> Emotes { get; set; }
}

public class DiscordUser
{
    public string Username { get; set; }
    public ulong DiscordId { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime DiscordTimestamp { get; set; }
}

public class DiscordChannel
{
    public string Name { get; set; }
    public ulong DiscordId { get; set; }
    public DateTime DiscordTimestamp { get; set; }

    public ICollection<DiscordMessageFull> Messages { get; set; }
}

public class DiscordMessageFull
{
    public ulong Id { get; set; }
    public ulong AuthorId { get; set; }
    public string? Content { get; set; }
    public bool HasAttatchment { get; set; }
    public bool IsEdited { get; set; }
    public bool IsRemoved { get; set; }
    public DateTime DiscordTimestamp { get; set; }
    public ulong? ReplyToMessageId { get; set; }
    
    public ICollection<DiscordReaction> Reactions { get; set; }
}

public class DiscordEmotes
{
    public string Name { get; set; }
    public ulong DiscordId { get; set; }
    public string? IconUrl { get; set; }
    public DateTime DiscordTimestamp { get; set; }
}