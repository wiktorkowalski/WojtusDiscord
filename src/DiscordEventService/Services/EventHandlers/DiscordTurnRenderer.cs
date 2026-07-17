using System.Diagnostics;
using System.Text;
using DSharpPlus.Entities;

namespace DiscordEventService.Services.EventHandlers;

// Renders one conversation turn as discrete Discord messages (#274): streamed deltas are
// buffered (never edited in live), each tool round posts one standalone message (the
// model's narration cue with the tool-batch summary as a subtext line), and the final
// answer is posted complete at the end. The typing indicator bridges the gaps — sending
// a message clears it, so it is re-triggered after every post and refreshed while a long
// round streams. Mentions are always suppressed: model text may contain @mentions.
internal sealed class DiscordTurnRenderer(DiscordChannel channel)
{
    // Headroom under Discord's hard 2000-char cap.
    private const int Limit = 1950;

    // Discord's typing indicator expires after ~10s; refresh under that while working.
    private static readonly TimeSpan TypingRefresh = TimeSpan.FromSeconds(8);

    private readonly StringBuilder _text = new();
    private long _lastTypingTimestamp = -1;

    // How many Discord messages this turn has actually posted (for the turn summary log).
    public int MessageCount { get; private set; }

    // Buffer a streamed token — no message I/O until a round boundary; just keep the
    // typing indicator alive so the channel shows the bot is working.
    public Task AppendDeltaAsync(string text)
    {
        _text.Append(text);
        return TriggerTypingAsync(force: false);
    }

    // A tool round finished: post its narration cue + summary as one standalone message,
    // then re-trigger typing for the next round's model call already in flight.
    public async Task CompleteRoundAsync(string summaryLine)
    {
        await PostAsync(ComposeRound(TakeBuffer(), summaryLine));
        await TriggerTypingAsync(force: true);
    }

    // The turn is over: whatever is buffered after the last tool round is the answer,
    // posted complete (chunked past the message cap).
    public Task CompleteTurnAsync() => PostAsync(TakeBuffer());

    // A round attempt failed after streaming partial deltas (#268): drop them so the
    // retried stream (or the failure line) renders exactly once. The buffer only ever
    // holds the current round — every round boundary already flushes it.
    public void ResetRound() => _text.Clear();

    public Task TriggerTypingAsync() => TriggerTypingAsync(force: true);

    // The round message: narration first, summary as the closing subtext line. A round
    // where the model streamed nothing visible still gets the summary alone.
    internal static string ComposeRound(string narration, string summaryLine) =>
        string.IsNullOrWhiteSpace(narration) ? summaryLine : $"{narration.TrimEnd()}\n{summaryLine}";

    private string TakeBuffer()
    {
        var content = _text.ToString();
        _text.Clear();
        return content;
    }

    private async Task PostAsync(string content)
    {
        // Never emit a blank Discord message — a whitespace-only buffer renders empty.
        if (string.IsNullOrWhiteSpace(content))
            return;

        foreach (var chunk in MessageChunker.Chunk(content, Limit))
        {
            await channel.SendMessageAsync(
                new DiscordMessageBuilder().WithContent(chunk).WithAllowedMentions(Mentions.None));
            MessageCount++;
        }
    }

    private async Task TriggerTypingAsync(bool force)
    {
        if (!force && _lastTypingTimestamp >= 0
            && Stopwatch.GetElapsedTime(_lastTypingTimestamp) < TypingRefresh)
            return;

        await channel.TriggerTypingAsync();
        _lastTypingTimestamp = Stopwatch.GetTimestamp();
    }
}
