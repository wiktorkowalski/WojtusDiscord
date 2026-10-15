namespace DiscordEventService.Data.Entities.Conversations;

// The usage ledger (#267): one row per model-call ATTEMPT (a turn is multiple rounds;
// §2 retries add attempt > 1 rows — failed attempts may still bill, so they are
// recorded, not dropped). This table is the §3 soft-cap source and the future
// enforcement seam.
public class ConversationUsageEntity
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    // Who triggered the turn — denormalized on purpose (#256 amendment): per-user
    // cost sums need a plain WHERE; joining through conversations misattributes
    // multi-invoker threads.
    public ulong InvokerId { get; set; }

    public int TurnIndex { get; set; }
    public int Round { get; set; }
    public int Attempt { get; set; }

    public string Model { get; set; } = "";

    // The whole input of the call: uncached + cache read + cache write (#426). Rows from the
    // OpenRouter era (before #426) hold OpenRouter's count, with no cache split.
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    // Prompt-cache split of PromptTokens (#426); null on OpenRouter-era rows and on a
    // failed attempt whose stream died before its usage event.
    public int? CacheReadTokens { get; set; }
    public int? CacheWriteTokens { get; set; }

    // USD. Since #426 computed by the bot from the tokens and Conversation:Prices, search
    // fee included; before #426 OpenRouter's billed usage.cost.
    public double? CostUsd { get; set; }

    // USD. Since #426 the token-only cost (CostUsd less the web search fee), so
    // CostUsd − UpstreamInferenceCostUsd stays the search spend (§5); before #426
    // OpenRouter's cost_details.upstream_inference_cost.
    public double? UpstreamInferenceCostUsd { get; set; }
    public int? WebSearchRequests { get; set; }

    public long LatencyMs { get; set; }

    // Mid-stream/pre-stream failure (§2 writes these; a failed attempt may still bill).
    public bool Failed { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
}
