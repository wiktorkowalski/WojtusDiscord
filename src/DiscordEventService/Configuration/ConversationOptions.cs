namespace DiscordEventService.Configuration;

// Configuration for the conversational assistant (#238, ADR-0006). Shares the
// OpenRouter ApiKey/BaseUrl from OpenRouterOptions; everything model- and
// conversation-specific lives here. Boot-safe when unconfigured — the handler
// short-circuits rather than failing startup.
internal sealed class ConversationOptions
{
    public const string SectionName = "Conversation";

    // The chat model — deliberately distinct from OpenRouterOptions.Model (the meme
    // vision model). Routing chat through the vision model would regress indexing.
    public string Model { get; set; } = "anthropic/claude-sonnet-4.6";

    // OpenRouter `reasoning.effort` for the chat model: low | medium | high.
    public string ReasoningEffort { get; set; } = "medium";

    // Hard ceiling on model->tool->model rounds in a single turn (#240). Guards
    // against a runaway tool loop; on the cap the bot makes one final tool-less call
    // so a turn always terminates with text rather than looping on tool_choice.
    public int MaxToolRounds { get; set; } = 8;

    // The guild whose ingested data the assistant answers about when there is no
    // ambient guild — i.e. in a DM. Lets meme_search (and later query_database) work
    // outside the server. Unset => those tools report they need a server context.
    public ulong? PrimaryGuildId { get; set; }

    // Guild channels where an @mention opens a conversation thread. Snowflakes, not
    // names. Empty => the bot only converses in DMs (threads it already owns still work).
    public ulong[] ChannelAllowList { get; set; } = [];

    // Users allowed to invoke admin/write actions. Captured out-of-band (never a model
    // parameter) so prompt injection cannot escalate. Consumed from §6 onward.
    public ulong[] AdminUserIds { get; set; } = [];

    // Optional system-prompt override; falls back to a built-in persona when unset.
    public string? SystemPrompt { get; set; }

    // Hard ceiling on a single turn's model round-trip.
    public int RequestTimeoutSeconds { get; set; } = 120;

    // Langfuse OTLP tracing (ADR-0006). All three are required to export; when any is
    // absent the feature still works, traces just aren't shipped.
    public string? LangfuseHost { get; set; }
    public string? LangfusePublicKey { get; set; }
    public string? LangfuseSecretKey { get; set; }

    public bool LangfuseConfigured =>
        !string.IsNullOrWhiteSpace(LangfuseHost)
        && !string.IsNullOrWhiteSpace(LangfusePublicKey)
        && !string.IsNullOrWhiteSpace(LangfuseSecretKey);
}
