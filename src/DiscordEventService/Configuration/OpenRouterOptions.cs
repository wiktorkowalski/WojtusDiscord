namespace DiscordEventService.Configuration;

internal sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    // The model used for indexing proper — the #219 benchmark winner (100/100,
    // best template/source recognition, ~$0.16/100 images). Preview id; if
    // renamed upstream this is config + stored per row, so the swap is cheap.
    // Benchmark runs ignore this and use BenchmarkModels.
    public string Model { get; set; } = "google/gemini-3-flash-preview";

    public string[] BenchmarkModels { get; set; } =
    [
        "google/gemini-2.5-flash",
        "google/gemini-3-flash-preview",
        "google/gemini-3.5-flash",
    ];

    public int RequestDelayMs { get; set; } = 250;

    // Reasoning models (gemini-3.5-flash) spend thinking tokens from the same
    // budget — 1500 truncated their JSON mid-string even at low effort.
    public int MaxOutputTokens { get; set; } = 4000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
