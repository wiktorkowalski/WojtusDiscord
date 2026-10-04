namespace DiscordEventService.Data.Entities.Core;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum MemeIndexStatus
{
    Pending = 0,
    Indexed = 1,
    Failed = 2,
    Skipped = 3
}

// One row per image attachment in a meme channel (an "Indexed meme",
// CONTEXT.md / ADR-0004 / ADR-0005) — a 3-image message yields 3 rows.
// Lifecycle only (#367): the vision metadata lives in meme_annotations.
public class MemeIndexEntity : ITimestamped
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    // Denormalized snowflakes: jump links + joins without touching messages.
    public ulong GuildDiscordId { get; set; }
    public ulong ChannelDiscordId { get; set; }
    public ulong MessageDiscordId { get; set; }

    // Natural idempotency key — an attachment is indexed at most once.
    public ulong AttachmentDiscordId { get; set; }

    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }

    // SHA-256 hex of the downloaded bytes; null until indexed. Dedupe handle.
    public string? ContentHash { get; set; }

    // Indexed = the attachment has at least one annotation. No CHECK can span the two
    // tables, so every writer adds the annotation and flips the status in one SaveChanges.
    public MemeIndexStatus Status { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }

    // The last writer that refused this image (#373): a refusal is the model's outcome, not the
    // attachment's. Both set or both null; independent of Status, so an Indexed row can carry it.
    // The manual backfill does not ask this (model, prompt version) again; any other one may try.
    public string? RefusedByModelId { get; set; }
    public string? RefusedByPromptVersion { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    public MessageEntity Message { get; set; } = null!;

    public List<MemeAnnotationEntity> Annotations { get; set; } = [];
}
