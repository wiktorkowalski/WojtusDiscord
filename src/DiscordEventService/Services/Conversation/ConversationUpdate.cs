namespace DiscordEventService.Services.Conversation;

// Render events the agentic loop yields as it works a turn (#238 §3); the handler maps
// each to a Discord message operation. A turn renders discretely (#274, #438): the
// model's narration before a tool call is one message, and every tool call is its own
// message after it — a tool round's calls at the round's end, a web search the moment its
// block is complete. The deltas after the last call are the answer, posted complete when
// the turn finishes. Keeping these Discord-free keeps ConversationService testable
// without a Discord client — the contract test just collects the events.
internal abstract record ConversationUpdate
{
    private ConversationUpdate()
    {
    }

    // A chunk of streamed assistant text — the handler buffers it until the next call
    // (a tool round, a web search) or the end of the turn.
    public sealed record AssistantTextDelta(string Text) : ConversationUpdate;

    // The tool calls of the current round ran; one line per call ("-# 🔧 meme_search"),
    // in the model's order. The handler posts the buffered narration, then each line as
    // its own message, and the next AssistantTextDelta starts the next round's buffer.
    public sealed record ToolCalls(IReadOnlyList<string> Lines) : ConversationUpdate;

    // The model called a server tool (web search) mid-stream; one line with its query
    // ("-# 🔎 web_search: …"). The handler posts the buffered text, then this line, and
    // the deltas after it fill the next message. The post is live: when the attempt then
    // fails and retries, its narration and search line can show twice.
    public sealed record ServerToolCall(string Line) : ConversationUpdate;

    // A round attempt failed after streaming partial text (#268): the handler drops the
    // buffered deltas so the retried stream (or the failure line) renders exactly once —
    // never appended after the discarded partial.
    public sealed record RoundReset : ConversationUpdate;
}
