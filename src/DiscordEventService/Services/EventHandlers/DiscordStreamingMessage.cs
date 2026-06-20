using System.Diagnostics;
using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;

namespace DiscordEventService.Services.EventHandlers;

// One streaming "bubble" of a conversation reply (#241): accumulates assistant text,
// edits a single Discord message in place at a throttled cadence, and spills into further
// messages when the content grows past Discord's per-message limit. Mentions are always
// suppressed — model text may contain @mentions, and an edit re-parses them otherwise, so
// every send AND every edit carries Mentions.None.
internal sealed class DiscordStreamingMessage(DiscordChannel channel, TimeSpan throttle)
{
    // Headroom under Discord's hard 2000-char cap so an in-flight edit never overflows.
    private const int Limit = 1950;

    private readonly StringBuilder _text = new();
    private readonly List<DiscordMessage> _messages = [];
    private readonly List<string> _rendered = [];
    private long _lastFlushTimestamp = -1;

    // Accumulate a streamed token; push to Discord only when the throttle window elapsed.
    public Task AppendDeltaAsync(string text, CancellationToken cancellationToken)
    {
        _text.Append(text);
        return MaybeFlushAsync(cancellationToken);
    }

    // A standalone line (the tool-batch summary) on its own row, surfaced immediately so
    // the user sees progress while the next round's model call is in flight.
    public Task AppendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (_text.Length > 0)
            _text.Append('\n');
        _text.Append(line);
        return FlushAsync(cancellationToken);
    }

    private Task MaybeFlushAsync(CancellationToken cancellationToken)
    {
        if (_text.Length == 0)
            return Task.CompletedTask;
        if (_lastFlushTimestamp >= 0 && Stopwatch.GetElapsedTime(_lastFlushTimestamp) < throttle)
            return Task.CompletedTask;
        return FlushAsync(cancellationToken);
    }

    // Push the current buffer to Discord now — the guaranteed final flush and the prompt
    // path for status lines. Earlier chunks stabilise once a later chunk exists, so they
    // are edited at most once; only the growing tail is re-edited each flush.
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_text.Length == 0)
            return;

        var chunks = MessageChunker.Chunk(_text.ToString(), Limit);
        for (var i = 0; i < chunks.Count; i++)
        {
            if (i < _messages.Count)
            {
                if (_rendered[i] == chunks[i])
                    continue;
                await _messages[i].ModifyAsync(Build(chunks[i]));
                _rendered[i] = chunks[i];
            }
            else
            {
                var message = await channel.SendMessageAsync(Build(chunks[i]));
                _messages.Add(message);
                _rendered.Add(chunks[i]);
            }
        }

        _lastFlushTimestamp = Stopwatch.GetTimestamp();
    }

    private static DiscordMessageBuilder Build(string content) =>
        new DiscordMessageBuilder().WithContent(content).WithAllowedMentions(Mentions.None);
}
