using System.ComponentModel.DataAnnotations;

namespace DiscordEventService.Configuration;

// Configuration for the conversational assistant (#238, ADR-0006, ADR-0007). The Claude API
// key lives in AnthropicOptions; everything model- and conversation-specific lives here.
// Boot-safe when unconfigured — the handler short-circuits rather than failing startup.
internal sealed class ConversationOptions
{
    public const string SectionName = "Conversation";

    // What the Claude API accepts for `output_config.effort`; anything else is a 400 on every call.
    public const string KnownReasoningEfforts = "low|medium|high|xhigh|max";

    // The Claude API model id. Its price must be in Prices, or the ledger cost stays null.
    public string Model { get; set; } = "claude-sonnet-5-5";

    // `output_config.effort` of every call; adaptive thinking decides how much to think within it.
    // Required: the regex alone lets an empty value through.
    [Required]
    [RegularExpression("^(" + KnownReasoningEfforts + ")$",
        ErrorMessage = "Conversation:ReasoningEffort must be one of " + KnownReasoningEfforts)]
    public string ReasoningEffort { get; set; } = "medium";

    // `max_tokens` of every call. Thinking counts toward it, so it is far above a Discord-sized
    // answer: a low limit cuts the answer off after a long think (stop_reason max_tokens).
    [Range(1024, 128000)]
    public int MaxOutputTokens { get; set; } = 16000;

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

    // §2 per-round model-call retry (#268): attempts per round (1 initial + retries),
    // exponential backoff with full jitter between transient failures. Worst case
    // (3 attempts, ≤1s+2s backoff) stays well under RequestTimeoutSeconds.
    public int RetryMaxAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public int RetryMaxDelayMs { get; set; } = 8000;

    // The visible localized failure line posted when a round's retries exhaust (or the
    // failure is terminal) — never fail a turn silently (#268).
    public string FailureMessage { get; set; } = "-# 💥 Coś się wysypało — spróbuj jeszcze raz.";

    // Conversation memory replay window (#267). The budget is summed over locally
    // estimated message sizes (chars/4, a zero-dependency proxy; the per-request usage of
    // the API can't size a per-message window). The Claude tokenizer counts more tokens per
    // character than the old GPT-normalized OpenRouter counts, so the budget is a soft
    // bound: compare it with the ledger's prompt_tokens (#426). WindowMaxMessages caps the
    // rows each turn loads before the budget trims them.
    // A thread is one conversation, so the window holds all of it in practice: a row cap of 40
    // let one person's spam push another person's messages out (#450). The token budget is the
    // real bound; chars/4 undercounts, so 80k estimated stays well under the model's context.
    public int WindowTokenBudget { get; set; } = 80000;
    public int WindowMaxMessages { get; set; } = 1000;

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

    // What query_database hides from members (#460): Postgres row-level security on the query role,
    // fed from these settings at boot (QuerySandboxSettingsWriter), so the model's SQL cannot skip it.
    public QueryDatabaseOptions QueryDatabase { get; set; } = new();

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

    // §3 soft cost-cap alerting (#269) over the conversation_usage ledger — alert-only,
    // the loop never refuses a turn.
    public CostAlertOptions CostAlerts { get; set; } = new();

    // The native Claude web search server tool (#271, #426). Attached to every loop round
    // when enabled — safe because a search bills only when the model runs one, never per
    // attached request.
    public WebSearchOptions WebSearch { get; set; } = new();

    // USD prices per model id (#426): the Claude API reports tokens, not money, so the
    // ledger cost is computed from these. A model with no entry gets a null cost and one
    // Warning. Source: https://platform.claude.com/docs/en/about-claude/pricing (2026-10-15).
    // The binder merges config keys into this default; it never removes one.
    public Dictionary<string, ModelPrice> Prices { get; set; } = new(StringComparer.Ordinal)
    {
        ["claude-sonnet-5-5"] = new ModelPrice
        {
            InputPerMTokUsd = 2,
            OutputPerMTokUsd = 10,
            CacheReadPerMTokUsd = 0.20,
            CacheWritePerMTokUsd = 2.50,
        },
        // The model before #435, kept so a rollback by Conversation__Model still gets a cost.
        ["claude-sonnet-5"] = new ModelPrice
        {
            InputPerMTokUsd = 2,
            OutputPerMTokUsd = 10,
            CacheReadPerMTokUsd = 0.20,
            CacheWritePerMTokUsd = 2.50,
        },
    };

    // The web search fee: $10 per 1,000 searches on the Claude API, any model.
    public double WebSearchPerThousandUsd { get; set; } = 10;
}

// Conversation:QueryDatabase:* (#460). Global switches, no per-turn bypass. A hidden channel id also
// hides every channel and thread under it (a category hides its channels, a channel its threads).
internal sealed class QueryDatabaseOptions
{
    public bool ExcludeDeletedMessages { get; set; } = true;

    public ulong[] HiddenChannelIds { get; set; } = [];
}

// One model's token prices in USD per million tokens. CacheWrite is the 5-minute TTL write
// price (1.25× input), the only TTL the loop uses.
internal sealed class ModelPrice
{
    public double InputPerMTokUsd { get; set; }
    public double OutputPerMTokUsd { get; set; }
    public double CacheReadPerMTokUsd { get; set; }
    public double CacheWritePerMTokUsd { get; set; }
}

// Conversation:WebSearch:* (#271, #426) → the `web_search_20260209` tool definition.
// MaxUses caps the searches per model call (more → a `max_uses_exceeded` result block, not
// an error); #269's soft caps cover the tail. Use AllowedDomains or BlockedDomains, never
// both (the API answers 400). UserLocation localizes results; empty fields are left out.
internal sealed class WebSearchOptions
{
    public bool Enabled { get; set; } = true;

    public int MaxUses { get; set; } = 5;

    public string[] AllowedDomains { get; set; } = [];

    public string[] BlockedDomains { get; set; } = [];

    public WebSearchLocationOptions UserLocation { get; set; } = new();
}

// The approximate location the searches are localized to. Country is an ISO 3166-1 alpha-2
// code, Timezone an IANA id. All empty → no user_location on the wire.
internal sealed class WebSearchLocationOptions
{
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; } = "PL";
    public string? Timezone { get; set; } = "Europe/Warsaw";
}

// Conversation:CostAlerts:* (#269). Windows are calendar-UTC (month / date); sums
// include failed and retried attempts (they may bill) and cover conversation spend
// only — meme indexing is a different ledger. A cap set to 0 or null is disabled.
internal sealed class CostAlertOptions
{
    public bool Enabled { get; set; } = true;

    // Monthly caps warn at 80% and 100%; the daily runaway tripwire at 100% only.
    public double? GlobalMonthlyUsd { get; set; } = 50;
    public double? PerUserMonthlyUsd { get; set; } = 15;
    public double? GlobalDailyUsd { get; set; } = 5;

    // How many per-invoker sums a global-cap alert lists as the spend breakdown.
    public int TopSpendersCount { get; set; } = 5;
}
