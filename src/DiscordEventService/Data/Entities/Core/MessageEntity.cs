using DiscordEventService.Data.Entities.Events;

namespace DiscordEventService.Data.Entities.Core;

public class MessageEntity : ITimestamped
{
    public Guid Id { get; set; }
    public ulong DiscordId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid GuildId { get; set; }
    public Guid AuthorId { get; set; }

    public string? Content { get; set; }
    public ulong? ReplyToDiscordId { get; set; }

    public bool HasAttachments { get; set; }
    public bool HasEmbeds { get; set; }
    public string? AttachmentsJson { get; set; }
    public string? EmbedsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Navigation properties - Core
    public GuildEntity Guild { get; set; } = null!;
    public ChannelEntity Channel { get; set; } = null!;
    public UserEntity Author { get; set; } = null!;

    // Navigation properties - Events (soft relations)
    public ICollection<MessageEventEntity> MessageEvents { get; set; } = [];
    public ICollection<ReactionEventEntity> ReactionEvents { get; set; } = [];
    public ICollection<PollEventEntity> PollEvents { get; set; } = [];
    public ICollection<MessageEditHistoryEntity> EditHistory { get; set; } = [];
}
