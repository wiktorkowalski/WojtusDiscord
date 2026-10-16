using System.Diagnostics;
using System.Text;
using DiscordEventService.Services.EventHandlers;

namespace DiscordEventService.Services.Conversation.Interaction;

// Renders one conversation turn as discrete messages (#274, #438): streamed deltas are
// buffered (never edited in live), the model's narration before a tool call posts as one
// message and every tool call as its own subtext line after it, and the final answer is
// posted complete at the end. The typing indicator bridges the gaps — sending
// a message clears it, so it is re-triggered after every post and refreshed while a long
// round streams. Writes through ITurnSurface (#308), so the whole render contract —
// chunking, typing refresh, the blank-message guard — is testable without Discord; the
// surface is what suppresses mentions, since model text may contain @mentions.
internal sealed class TurnRenderer(ITurnSurface surface)
{
    // Headroom under Discord's hard 2000-char cap.
    private const int Limit = 1950;

    // Discord's typing indicator expires after ~10s; refresh under that while working.
    private static readonly TimeSpan TypingRefresh = TimeSpan.FromSeconds(8);

    private readonly StringBuilder _text = new();
    private long _lastTypingTimestamp = -1;

    // How many messages this turn has actually posted (for the turn summary log).
    public int MessageCount { get; private set; }

    // Maps one render event of the loop to its message operation.
    public Task ApplyAsync(ConversationUpdate update)
    {
        switch (update)
        {
            case ConversationUpdate.AssistantTextDelta delta:
                return AppendDeltaAsync(delta.Text);
            case ConversationUpdate.ToolCalls toolCalls:
                return CompleteRoundAsync(toolCalls.Lines);
            case ConversationUpdate.ServerToolCall serverToolCall:
                return PostServerToolCallAsync(serverToolCall.Line);
            case ConversationUpdate.RoundReset:
                ResetRound();
                return Task.CompletedTask;
            default:
                throw new ArgumentOutOfRangeException(nameof(update), update, "Unknown conversation update");
        }
    }

    // Buffer a streamed token — no message I/O until a round boundary; just keep the
    // typing indicator alive so the channel shows the bot is working.
    public Task AppendDeltaAsync(string text)
    {
        _text.Append(text);
        return TriggerTypingAsync(force: false);
    }

    // A tool round finished: post its narration cue, then one message per tool call, and
    // re-trigger typing after each — the next round's model call is already in flight.
    public async Task CompleteRoundAsync(IReadOnlyList<string> callLines)
    {
        await PostAndTypeAsync(TakeBuffer());
        foreach (var line in callLines)
            await PostAndTypeAsync(line);
    }

    // A web search started mid-stream: post the text before it, then the search line; the
    // deltas after it start the next message.
    public async Task PostServerToolCallAsync(string line)
    {
        await PostAndTypeAsync(TakeBuffer());
        await PostAndTypeAsync(line);
    }

    // The turn is over: whatever is buffered after the last tool round is the answer,
    // posted complete (chunked past the message cap).
    public Task CompleteTurnAsync() => PostAsync(TakeBuffer());

    // A round attempt failed after streaming partial deltas (#268): drop them so the
    // retried stream (or the failure line) renders exactly once. Every tool round boundary
    // flushes the buffer; a pause_turn does not, so the loop re-sends the paused text after
    // the reset (#426).
    public void ResetRound() => _text.Clear();

    public Task TriggerTypingAsync() => TriggerTypingAsync(force: true);

    private string TakeBuffer()
    {
        var content = _text.ToString();
        _text.Clear();
        return content;
    }

    // Returns false when there was nothing to post.
    private async Task<bool> PostAsync(string content)
    {
        // Never emit a blank message — a whitespace-only buffer renders empty.
        if (string.IsNullOrWhiteSpace(content))
            return false;

        foreach (var chunk in MessageChunker.Chunk(content, Limit))
        {
            await surface.SendAsync(chunk);
            MessageCount++;
        }
        return true;
    }

    // Sending clears the typing indicator; bring it back after every round message.
    private async Task PostAndTypeAsync(string content)
    {
        if (await PostAsync(content))
            await TriggerTypingAsync(force: true);
    }

    private async Task TriggerTypingAsync(bool force)
    {
        if (!force && _lastTypingTimestamp >= 0
            && Stopwatch.GetElapsedTime(_lastTypingTimestamp) < TypingRefresh)
            return;

        await surface.TriggerTypingAsync();
        _lastTypingTimestamp = Stopwatch.GetTimestamp();
    }
}
