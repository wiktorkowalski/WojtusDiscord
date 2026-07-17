namespace DiscordEventService.Services.Conversation;

// Render events the agentic loop yields as it works a turn (#238 §3); the handler maps
// each to a Discord message operation. A turn renders discretely (#274): a tool round
// posts one standalone message (the model's pre-tool narration plus the tool-batch
// summary line), and the deltas after the last tool round are the answer, posted
// complete when the turn finishes. Keeping these Discord-free keeps ConversationService
// testable without a Discord client — the contract test just collects the events.
internal abstract record ConversationUpdate
{
    private ConversationUpdate()
    {
    }

    // A chunk of streamed assistant text — the handler buffers it until the round's
    // boundary (a tool-batch summary, or the end of the turn).
    public sealed record AssistantTextDelta(string Text) : ConversationUpdate;

    // The tool batch for the current round finished; a one-line summary ("🔧 meme_search")
    // that closes the round — the handler posts the buffered narration plus this line as
    // one message, and the next AssistantTextDelta starts the next round's buffer.
    public sealed record ToolBatchSummary(string Text) : ConversationUpdate;
}
