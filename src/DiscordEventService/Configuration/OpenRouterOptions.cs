using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Configuration;

internal sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    // What OpenRouter accepts for `reasoning.effort`; anything else is an HTTP 400 on every call.
    public const string KnownReasoningEfforts = "none|minimal|low|medium|high|xhigh|max";

    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    // The model used for indexing proper — the #219 benchmark winner (100/100,
    // best template/source recognition, ~$0.16/100 images). Preview id; if
    // renamed upstream this is config + stored per row, so the swap is cheap.
    // Benchmark runs ignore this and use BenchmarkModels.
    public string Model { get; set; } = "google/gemini-3-flash-preview";

    // Unset (null or "") sends no `reasoning` field: the model runs at its own default, which for
    // gemini-3.8-flash is medium — dearer and no better than low (#366). Benchmark runs ignore this.
    [RegularExpression("^(" + KnownReasoningEfforts + ")$",
        ErrorMessage = "OpenRouter:ReasoningEffort must be one of " + KnownReasoningEfforts + ", or unset")]
    public string? ReasoningEffort { get; set; }

    // Slots: "model" or "model|effort=low". Empty on purpose — the binder APPENDS BenchmarkModels__N
    // to a code default instead of replacing it, so a default list could never be dropped from a run.
    public string[] BenchmarkModels { get; set; } = [];

    public int RequestDelayMs { get; set; } = 250;

    // Reasoning models (gemini-3.5-flash) spend thinking tokens from the same
    // budget — 1500 truncated their JSON mid-string even at low effort.
    public int MaxOutputTokens { get; set; } = 4000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static bool IsKnownReasoningEffort(string value) => KnownReasoningEfforts.Split('|').Contains(value);
}
