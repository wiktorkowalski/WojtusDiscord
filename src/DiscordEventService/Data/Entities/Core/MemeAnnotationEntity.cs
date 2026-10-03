using System.Text.Json.Serialization;
using NpgsqlTypes;

namespace DiscordEventService.Data.Entities.Core;

// Schema v2 (#368). The JSON names are the model contract and the strict json_schema enum lists.
public enum MemeImageKind
{
    [JsonStringEnumMemberName("template_meme")]
    TemplateMeme = 0,

    [JsonStringEnumMemberName("screenshot_post_or_chat")]
    ScreenshotPostOrChat = 1,

    [JsonStringEnumMemberName("comic")]
    Comic = 2,

    [JsonStringEnumMemberName("photo_with_caption")]
    PhotoWithCaption = 3,

    // No names on these (#368): MemeMetadataSanitizer drops people and name tags before storing.
    [JsonStringEnumMemberName("cutout_face_or_emote")]
    CutoutFaceOrEmote = 4,

    [JsonStringEnumMemberName("edited_photo")]
    EditedPhoto = 5,

    [JsonStringEnumMemberName("video_frame")]
    VideoFrame = 6,

    [JsonStringEnumMemberName("other")]
    Other = 7
}

public enum MemeLanguage
{
    [JsonStringEnumMemberName("pl")]
    Pl = 0,

    [JsonStringEnumMemberName("en")]
    En = 1,

    [JsonStringEnumMemberName("mixed")]
    Mixed = 2,

    // The image has no text.
    [JsonStringEnumMemberName("none")]
    None = 3
}

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
    public string[] Templates { get; set; } = [];
    public string[] SearchPhrases { get; set; } = [];

    // jsonb: [{ "name": ..., "evidence": ... }].
    public string People { get; set; } = "[]";

    // Filled from People at write time. The generated columns read it through
    // f_text_array_join; names inside jsonb would need one more IMMUTABLE function.
    public string[] PeopleNames { get; set; } = [];

    // Null = written before schema v2: unknown, which is not the same as Other / None.
    public MemeImageKind? ImageKind { get; set; }
    public MemeLanguage? Language { get; set; }

    public string? Franchise { get; set; }

    // One of MemeSources.Known; null = no platform visible.
    public string? Source { get; set; }

    // The model's verbatim output; for a cut-out the sanitised metadata instead (#368).
    // Null on a repost copy — the original row keeps it.
    public string? RawResponseJson { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }

    // Stored generated columns (#220 binding design comment) — never set from
    // code; the database derives them from the metadata columns.
    public NpgsqlTsVector SearchVector { get; set; } = null!;
    public string SearchText { get; set; } = null!;

    public MemeIndexEntity MemeIndex { get; set; } = null!;
}
