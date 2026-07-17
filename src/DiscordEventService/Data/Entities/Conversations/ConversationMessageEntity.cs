namespace DiscordEventService.Data.Entities.Conversations;

// Persisted as int in the DB — values are a data contract; never renumber or strip explicit values.
public enum ConversationMessageRole
{
    User = 0,
    Assistant = 1,
    Tool = 2
}

// One row per ChatMessage the agentic loop appended (#267) — NOT one row per tool
// call: an assistant message bundles N FunctionCallContent (its ToolCallsJson array),
// while each tool result is already its own ChatMessage and gets its own row.
// Append-only; the uuidv7 PK doubles as insertion order within a conversation.
public class ConversationMessageEntity
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    // Monotonic per conversation: every message persisted during one
    // GenerateReplyAsync turn (user + assistant rounds + tool results) shares it.
    public int TurnIndex { get; set; }

    public ConversationMessageRole Role { get; set; }

    // The message's text content; null for a pure tool-call assistant message
    // and for tool rows.
    public string? Text { get; set; }

    // Assistant rows only: JSON array of { id, name, argumentsJson } — arguments kept
    // as raw JSON so rehydration rebuilds FunctionCallContent without re-serializing
    // a dictionary of JsonElements.
    public string? ToolCallsJson { get; set; }

    // Tool rows only. ToolResult is the tool's raw string, stored as plain text —
    // NEVER a serialized ChatMessage blob: rehydrating FunctionResultContent with a
    // string Result keeps the OpenAI adapter's `as string` fast-path and byte-identical
    // wire replay (a JsonElement Result replays quote-wrapped/escaped).
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    public string? ToolResult { get; set; }

    // Reasoning content persisted for the record but STRIPPED on replay — stale
    // thinking signatures aren't echoed back (research A-OQ2); revisit if reply
    // quality suffers.
    public string? Reasoning { get; set; }

    // Provider-reported usage for the round that produced this message (assistant
    // rows); null otherwise.
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    // Local chars/4 estimate — the window budget currency (the per-request provider
    // usage can't size a per-message window).
    public int EstTokens { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ConversationEntity Conversation { get; set; } = null!;
}
