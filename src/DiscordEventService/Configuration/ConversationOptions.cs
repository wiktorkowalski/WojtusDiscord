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

    // Streaming cadence (#241): the round's message is edited in place at most this often
    // while text streams in (Discord rate limits), with a guaranteed final flush.
    public int StreamEditThrottleMs { get; set; } = 750;

    // Shown as the per-round interim line when the model calls a tool without first
    // narrating, so a tool round always has a visible "working on it" message. Discord
    // subtext via the `-#` prefix.
    public string InterimNarration { get; set; } = "-# 🔍 już sprawdzam…";

    // Hard ceiling on a single turn's model round-trip.
    public int RequestTimeoutSeconds { get; set; } = 120;

    // query_database (#238 §4). The query runs inside a read-only transaction that first drops to
    // QueryRoleName via SET LOCAL ROLE — a non-superuser, SELECT-only role — so the model's SQL can
    // neither write nor reach privileged/file functions (the app login is a superuser; the read-only
    // txn alone would not stop pg_read_file etc.). Rows are capped (read N+1, truncate); each value is
    // length-capped. The client CommandTimeout (QueryTimeoutSeconds) stays UNDER the per-query
    // server-side statement_timeout (QueryServerTimeoutSeconds) so the client cancels first.
    public string QueryRoleName { get; set; } = "wojtus_query";
    public int QueryRowLimit { get; set; } = 100;
    public int QueryTimeoutSeconds { get; set; } = 10;
    public int QueryServerTimeoutSeconds { get; set; } = 15;

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
