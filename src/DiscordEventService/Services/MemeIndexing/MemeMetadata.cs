using System.Text.Json.Serialization;

namespace DiscordEventService.Services.MemeIndexing;

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

    // Platform watermark/UI visible in the image (reddit, kwejk, ...), if any.
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    // Canonical meme template name (drake, distracted boyfriend, ...), if any.
    [JsonPropertyName("template")]
    public string? Template { get; init; }
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
    // meme_index.raw_response_json (#221).
    public string? RawContent { get; init; }

    public static MemeAnalysisResult Success(MemeMetadata metadata, MemeAnalysisUsage usage, string? rawContent = null) =>
        new MemeAnalysisResult { Outcome = MemeAnalysisOutcome.Success, Metadata = metadata, Usage = usage, RawContent = rawContent };

    public static MemeAnalysisResult Refusal(string reason) =>
        new MemeAnalysisResult { Outcome = MemeAnalysisOutcome.Refusal, Error = reason };

    public static MemeAnalysisResult Failed(string error, bool isTransient) =>
        new MemeAnalysisResult { Outcome = MemeAnalysisOutcome.Error, Error = error, IsTransient = isTransient };
}
