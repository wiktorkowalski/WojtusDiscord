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

    // Whether a user may invoke action tools (#238 §6). The single source of truth for the
    // admin gate, read both when building the invocation context (the invoker) and when a
    // confirm button is clicked (the clicker) — never a model parameter, so a prompt-injected
    // member can neither escalate nor self-confirm a staged action.
    public bool IsAdmin(ulong userId) => Array.IndexOf(AdminUserIds, userId) >= 0;

    // A staged irreversible action (#238 §6) expires if no admin confirms within this window.
    // The pending action is in-memory only and is also dropped on restart.
    public int ConfirmExpirySeconds { get; set; } = 300;

    // Optional system-prompt override; falls back to a built-in persona when unset.
    public string? SystemPrompt { get; set; }

    // Hard ceiling on a single turn's model round-trip.
    public int RequestTimeoutSeconds { get; set; } = 120;

    // Conversation memory replay window (#267). The budget is summed over locally
    // estimated message sizes (chars/4 — OpenRouter normalizes counts to a GPT
    // tokenizer, so it's a fair proxy; the per-request provider usage can't size a
    // per-message window). WindowMaxMessages is the row backstop against a few huge
    // tool dumps blowing the estimate.
    public int WindowTokenBudget { get; set; } = 12000;
    public int WindowMaxMessages { get; set; } = 40;

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

    // Include full prompts, tool arguments and results in traces (#257). Development
    // always captures them regardless of this flag; prod opts in explicitly — the
    // Langfuse instance is LAN-only, so payloads never leave the home network.
    public bool EnableSensitiveData { get; set; }
}
