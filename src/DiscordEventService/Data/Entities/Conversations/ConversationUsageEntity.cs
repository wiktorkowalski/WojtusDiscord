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

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    // OpenRouter usage.cost (USD) recovered from the raw ChatTokenUsage patch.
    public double? CostUsd { get; set; }

    // Itemised §5 server-tool spend (cost_details / server-tool usage details);
    // null until the provider reports them.
    public double? UpstreamInferenceCostUsd { get; set; }
    public int? WebSearchRequests { get; set; }

    public long LatencyMs { get; set; }

    // Mid-stream/pre-stream failure (§2 writes these; a failed attempt may still bill).
    public bool Failed { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
}
