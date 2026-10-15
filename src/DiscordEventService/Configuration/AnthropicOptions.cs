namespace DiscordEventService.Configuration;

// The Claude API key of the conversational assistant (#426, ADR-0007). Meme indexing keeps its
// own OpenRouter key (OpenRouterOptions). ANTHROPIC_API_KEY is accepted as a fallback for
// Anthropic__ApiKey. Boot-safe when unset: the assistant stays inert.
internal sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    // The conventional variable name, read when Anthropic:ApiKey is empty.
    public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
