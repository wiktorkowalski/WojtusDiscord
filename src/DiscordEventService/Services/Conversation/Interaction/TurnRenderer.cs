using System.Diagnostics;
using System.Text;

namespace DiscordEventService.Services.Conversation.Interaction;

// Renders one conversation turn as discrete messages (#274, #438): streamed deltas are
// buffered (never edited in live), the model's narration before a tool call posts as one
// message and every tool call as its own subtext line after it, and the final answer is
// posted complete at the end. The typing indicator bridges the gaps: a renderer-owned timer
// (#456) keeps it alive through thinking, tool runs and retry backoff, which yield no text,
// and brings it back one tick after a burst of posts (a send clears it). Writes through
// ITurnSurface (#308), so the whole render contract — chunking, typing refresh, the
// blank-message guard — is testable without Discord; the surface is what suppresses
// mentions, since model text may contain @mentions.
internal sealed class TurnRenderer(ITurnSurface surface, ILogger logger, TimeSpan? typingRefresh = null)
    : IAsyncDisposable
{
    // Headroom under Discord's hard 2000-char cap.
    private const int Limit = 1950;

    // Discord's typing indicator expires after ~10s; refresh under that while working.
    private static readonly TimeSpan DefaultTypingRefresh = TimeSpan.FromSeconds(8);

    // Ticks per refresh window: how soon after a post the indicator comes back.
    private const int TypingTicksPerRefresh = 8;

    private readonly TimeSpan _typingRefresh = typingRefresh ?? DefaultTypingRefresh;

    private readonly StringBuilder _text = new();
    private readonly CancellationTokenSource _typingStop = new();
    private Task? _typingLoop;
    private long _lastTypingTimestamp;
    private volatile bool _postedSinceTyping;

    // How many messages this turn has actually posted (for the turn summary log).
    public int MessageCount { get; private set; }

    // Maps one render event of the loop to its message operation.
    public Task ApplyAsync(ConversationUpdate update)
    {
        switch (update)
        {
            case ConversationUpdate.AssistantTextDelta delta:
                AppendDelta(delta.Text);
                return Task.CompletedTask;
            case ConversationUpdate.ToolCall toolCall:
                return PostToolCallAsync(toolCall.Line);
            case ConversationUpdate.RoundReset reset:
                ResetRound(reset.Length);
                return Task.CompletedTask;
            default:
                throw new ArgumentOutOfRangeException(nameof(update), update, "Unknown conversation update");
        }
    }

    // Shows the bot working at once and keeps the indicator alive until the turn completes.
    public void StartTyping() => _typingLoop ??= KeepTypingAsync(_typingStop.Token);

    // Buffer a streamed token — no message I/O until a round boundary.
    public void AppendDelta(string text) => _text.Append(text);

    // A tool call: post the narration before it, then the call's line. No typing POST per
    // message (#456): the timer re-triggers it once after the round's last line.
    public async Task PostToolCallAsync(string line)
    {
        await PostAsync(TakeBuffer());
        await PostAsync(line);
    }

    // The turn is over: whatever is buffered after the last tool round is the answer,
    // posted complete (chunked past the message cap). Typing stops first, so no late
    // typing POST lands after the answer and shows the bot working on nothing.
    public async Task CompleteTurnAsync()
    {
        await StopTypingAsync();
        await PostAsync(TakeBuffer());
    }

    // A round attempt failed after streaming partial deltas (#268): drop its `length` buffered
    // characters so the retried stream (or the failure line) renders exactly once. The text
    // before them stays: a pause_turn does not flush the buffer (#426).
    public void ResetRound(int length) => _text.Length -= Math.Min(length, _text.Length);

    public async ValueTask DisposeAsync()
    {
        await StopTypingAsync();
        _typingStop.Dispose();
    }

    private string TakeBuffer()
    {
        var content = _text.ToString();
        _text.Clear();
        return content;
    }

    private async Task PostAsync(string content)
    {
        // Never emit a blank message — a whitespace-only buffer renders empty.
        if (string.IsNullOrWhiteSpace(content))
            return;

        foreach (var chunk in MessageChunker.Chunk(content, Limit))
        {
            await surface.SendAsync(chunk);
            MessageCount++;
            _postedSinceTyping = true;
        }
    }

    // A quiet stretch costs one typing POST per refresh window; a post brings it back a tick later.
    private async Task KeepTypingAsync(CancellationToken stop)
    {
        await TriggerTypingAsync();
        using var timer = new PeriodicTimer(_typingRefresh / TypingTicksPerRefresh);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                if (_postedSinceTyping || Stopwatch.GetElapsedTime(_lastTypingTimestamp) >= _typingRefresh)
                    await TriggerTypingAsync();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    // Awaits the loop, so a typing POST in flight lands before the answer that clears it.
    private async Task StopTypingAsync()
    {
        if (_typingLoop is null)
            return;
        await _typingStop.CancelAsync();
        await _typingLoop;
    }

    private async Task TriggerTypingAsync()
    {
        _postedSinceTyping = false;
        _lastTypingTimestamp = Stopwatch.GetTimestamp();

        // Best effort (#445): a 429/5xx on the typing POST must not end the turn mid-answer.
        try
        {
            await surface.TriggerTypingAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Typing indicator failed in channel {ChannelId}", surface.ChannelId);
        }
    }
}
