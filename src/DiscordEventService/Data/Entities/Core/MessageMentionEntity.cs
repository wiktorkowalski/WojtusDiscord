namespace DiscordEventService.Data.Entities.Core;

public enum MessageMentionType
{
    User = 0,
    Role = 1,
    Everyone = 2,
    Here = 3
}

public class MessageMentionEntity
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public ulong? MentionedUserDiscordId { get; set; }
    public ulong? MentionedRoleDiscordId { get; set; }
    public MessageMentionType MentionType { get; set; }

    // Navigation properties
    public MessageEntity Message { get; set; } = null!;
}
