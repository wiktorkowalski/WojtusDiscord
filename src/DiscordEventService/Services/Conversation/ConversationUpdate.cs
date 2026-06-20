namespace DiscordEventService.Services.Conversation;

// Render events the agentic loop yields as it works a turn (#238 §3); the handler maps
// each to a Discord message operation. A turn renders as one streamed message per round:
// a tool round leaves a narration message (the model's pre-tool text, then a tool-batch
// summary line appended), and the final round's message is the answer typed in live.
// Keeping these Discord-free keeps ConversationService testable without a Discord client
// — the contract test just collects the events.
internal abstract record ConversationUpdate
{
    private ConversationUpdate()
    {
    }

    // A chunk of streamed assistant text, appended to the current round's message — the
    // handler edits that message in place at a throttled cadence.
    public sealed record AssistantTextDelta(string Text) : ConversationUpdate;

    // The tool batch for the current round finished; a one-line summary appended to the
    // round's message ("🔧 meme_search"). Also marks the round's message complete — the
    // next AssistantTextDelta starts a fresh message (the next round, or the answer).
    public sealed record ToolBatchSummary(string Text) : ConversationUpdate;
}
