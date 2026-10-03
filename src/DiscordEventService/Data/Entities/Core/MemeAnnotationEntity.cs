using NpgsqlTypes;

namespace DiscordEventService.Data.Entities.Core;

// One writer's metadata for an indexed meme (#367): at most one row per
// (attachment, model, prompt version), N rows per attachment. Search ranks an
// attachment by its best annotation, so a new model adds rows instead of replacing them.
public class MemeAnnotationEntity : ITimestamped
{
    public Guid Id { get; set; }

    public Guid MemeIndexId { get; set; }

    // Denormalized from the status row: the natural key reads without a join.
    public ulong AttachmentDiscordId { get; set; }

    // Free text on purpose — an external writer (import) names its own model.
    public string ModelId { get; set; } = "";
    public string PromptVersion { get; set; } = "";

    // Provenance only, not part of the key. Null = the model ran at its own default effort.
    public string? ReasoningEffort { get; set; }

    public DateTime IndexedAtUtc { get; set; }

    // Vision metadata (MemeMetadata shape).
    public string DescriptionPl { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string OcrText { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string? Source { get; set; }
    public string? Template { get; set; }

    // The model's verbatim output. Null on a repost copy — the original row keeps it.
    public string? RawResponseJson { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Stored generated columns (#220 binding design comment) — never set from
    // code; the database derives them from the metadata columns.
    public NpgsqlTsVector SearchVector { get; set; } = null!;
    public string SearchText { get; set; } = null!;

    public MemeIndexEntity MemeIndex { get; set; } = null!;
}
