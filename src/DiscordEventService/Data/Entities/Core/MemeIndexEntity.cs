using NpgsqlTypes;

namespace DiscordEventService.Data.Entities.Core;

public enum MemeIndexStatus
{
    Pending = 0,
    Indexed = 1,
    Failed = 2,
    Skipped = 3
}

// One row per image attachment in a meme channel (an "Indexed meme",
// CONTEXT.md / ADR-0004 / ADR-0005) — a 3-image message yields 3 rows.
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

    // Vision metadata (MemeMetadata shape); null/empty until status = Indexed.
    public string? DescriptionPl { get; set; }
    public string? DescriptionEn { get; set; }
    public string? OcrText { get; set; }
    public string[] Tags { get; set; } = [];
    public string? Source { get; set; }
    public string? Template { get; set; }

    // Provenance: which model produced the metadata, and its raw response.
    public string? ModelId { get; set; }
    public string? RawResponseJson { get; set; }
    public DateTime? IndexedAtUtc { get; set; }

    public MemeIndexStatus Status { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Stored generated columns (#220 binding design comment) — never set from
    // code; the database derives them from the metadata columns.
    public NpgsqlTsVector SearchVector { get; set; } = null!;
    public string SearchText { get; set; } = null!;

    public MessageEntity Message { get; set; } = null!;
}
