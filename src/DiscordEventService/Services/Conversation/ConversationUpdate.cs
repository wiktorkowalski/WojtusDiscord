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

    // A chunk of streamed assistant text — the handler buffers it until the next tool call
    // or the end of the turn.
    public sealed record AssistantTextDelta(string Text) : ConversationUpdate;

    // One tool call, in the model's order: a line like "-# 🔧 meme_search" once the round's
    // tools ran, or "-# 🔎 web_search: …" the moment a search block is complete. The handler
    // posts the buffered text, then this line, and the next delta starts a new message. A
    // search line posts mid-attempt: when that attempt fails and retries, its narration and
    // search line can show twice.
    public sealed record ToolCall(string Line) : ConversationUpdate;

    // A round attempt failed after streaming partial text (#268): the handler drops the last
    // Length buffered characters (this attempt's deltas) so the retried stream (or the
    // failure line) renders exactly once — never appended after the discarded partial.
    public sealed record RoundReset(int Length) : ConversationUpdate;
}
