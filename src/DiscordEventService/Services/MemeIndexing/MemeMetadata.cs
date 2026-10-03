using System.Text.Json.Serialization;
using DiscordEventService.Data.Entities.Core;

namespace DiscordEventService.Services.MemeIndexing;

// The model contract, schema v2 (#368). Every member is required in the strict json_schema;
// `required` here is presence-only, so MemeAttachmentIndexer still rejects an explicit null.
internal sealed record MemeMetadata
{
    [JsonPropertyName("description_pl")]
    public required string DescriptionPl { get; init; }

    [JsonPropertyName("description_en")]
    public required string DescriptionEn { get; init; }

    // Verbatim text visible in the image, original language; "" when none.
    [JsonPropertyName("ocr_text")]
    public required string OcrText { get; init; }

    [JsonPropertyName("tags")]
    public required string[] Tags { get; init; }

    // Nullable for the repost copy of an annotation written before schema v2; the model never sends null.
    [JsonPropertyName("image_kind")]
    [JsonConverter(typeof(ClosedEnumJsonConverter<MemeImageKind>))]
    public required MemeImageKind? ImageKind { get; init; }

    // Canonical meme template names (drake, distracted boyfriend, ...); empty when none.
    [JsonPropertyName("templates")]
    public required string[] Templates { get; init; }

    [JsonPropertyName("people")]
    public required MemePerson[] People { get; init; }

    [JsonPropertyName("search_phrases")]
    public required string[] SearchPhrases { get; init; }

    [JsonPropertyName("franchise")]
    public required string? Franchise { get; init; }

    // Platform watermark/UI visible in the image: one of MemeSources.Known, null when none.
    [JsonPropertyName("source")]
    [JsonConverter(typeof(MemeSourceJsonConverter))]
    public required string? Source { get; init; }

    [JsonPropertyName("language")]
    [JsonConverter(typeof(ClosedEnumJsonConverter<MemeLanguage>))]
    public required MemeLanguage? Language { get; init; }
}

internal enum MemePersonEvidence
{
    [JsonStringEnumMemberName("name_visible")]
    NameVisible = 0,

    [JsonStringEnumMemberName("widely_recognized")]
    WidelyRecognized = 1
}

internal sealed record MemePerson
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("evidence")]
    [JsonConverter(typeof(ClosedEnumJsonConverter<MemePersonEvidence>))]
    public required MemePersonEvidence Evidence { get; init; }
}

internal enum MemeAnalysisOutcome
{
    Success,

    // The model declined to describe the image (safety filter). Terminal —
    // retrying the same image is pointless; §3 maps this to status Skipped.
    Refusal,

    // Transport/API/parse failure. Transient flavours are retryable.
    Error,
}

internal sealed record MemeAnalysisUsage(int PromptTokens, int CompletionTokens, decimal? CostUsd);

internal sealed record MemeAnalysisResult
{
    public required MemeAnalysisOutcome Outcome { get; init; }
    public MemeMetadata? Metadata { get; init; }
    public MemeAnalysisUsage? Usage { get; init; }
    public string? Error { get; init; }
    public bool IsTransient { get; init; }

    // The model's verbatim structured-output JSON — provenance for
    // meme_annotations.raw_response_json (#221, #367). The writer replaces it for a cut-out (#368).
    public string? RawContent { get; init; }

    public static MemeAnalysisResult Success(MemeMetadata metadata, MemeAnalysisUsage usage, string? rawContent = null) =>
        new MemeAnalysisResult { Outcome = MemeAnalysisOutcome.Success, Metadata = metadata, Usage = usage, RawContent = rawContent };

    public static MemeAnalysisResult Refusal(string reason) =>
        new MemeAnalysisResult { Outcome = MemeAnalysisOutcome.Refusal, Error = reason };

    public static MemeAnalysisResult Failed(string error, bool isTransient) =>
        new MemeAnalysisResult { Outcome = MemeAnalysisOutcome.Error, Error = error, IsTransient = isTransient };
}
