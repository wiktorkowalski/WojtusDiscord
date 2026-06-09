namespace DiscordEventService.Configuration;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    // The model used for indexing proper. Empty until the §1 benchmark decides
    // the winner (#219); benchmark runs ignore this and use BenchmarkModels.
    public string Model { get; set; } = "";

    public string[] BenchmarkModels { get; set; } =
    [
        "google/gemini-2.5-flash",
        "google/gemini-3-flash-preview",
        "google/gemini-3.5-flash"
    ];

    public int RequestDelayMs { get; set; } = 250;
    public int MaxOutputTokens { get; set; } = 1500;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
