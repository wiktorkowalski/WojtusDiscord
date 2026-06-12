namespace DiscordEventService.Data.Entities.Core;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum MessageMentionType
{
    User = 0,
    Role = 1,
    Everyone = 2,
    Here = 3,
    Channel = 4
}

public class MessageMentionEntity
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public ulong? MentionedUserDiscordId { get; set; }
    public ulong? MentionedRoleDiscordId { get; set; }
    public ulong? MentionedChannelDiscordId { get; set; }
    public MessageMentionType MentionType { get; set; }

    // Navigation properties
    public MessageEntity Message { get; set; } = null!;
}
