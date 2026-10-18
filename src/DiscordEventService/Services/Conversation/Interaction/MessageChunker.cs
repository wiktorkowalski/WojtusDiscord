namespace DiscordEventService.Services.Conversation.Interaction;

// Splits a reply into Discord-sized chunks for TurnRenderer (#274, #447). Breaks on a
// newline, else a space, never in the first quarter of the window, else hard-cuts outside
// a surrogate pair. A cut inside a ``` block closes the fence in this chunk and reopens it,
// language tag included, in the next, so every chunk renders its code as code.
internal static class MessageChunker
{
    private const string Fence = "```";

    // A break in the first 1/N of the window would post a tiny message; WebSearchCitations relies on it.
    private const int MinBreakFraction = 4;

    // The reopen repeats only the fence and its language tag: a long opener line would eat the budget.
    private const int MaxFenceTagLength = 20;

    public static List<string> Chunk(string content, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<string> chunks = [];
        string? openFence = null;
        var start = SkipWhitespace(content, 0);

        while (start < content.Length)
        {
            var reopen = openFence is null ? "" : openFence + "\n";
            if (reopen.Length + content.Length - start <= limit)
            {
                chunks.Add(reopen + content[start..]);
                break;
            }

            // Headroom for the "\n```" that closes a fence the cut falls inside.
            var budget = Math.Max(2, limit - reopen.Length - Fence.Length - 1);
            var breakAt = BreakPoint(content, start, budget);
            var piece = content.Substring(start, breakAt);
            var fenceAfter = FenceAfter(piece, openFence);

            var chunk = reopen + piece.TrimEnd();
            if (fenceAfter is not null)
                chunk += "\n" + Fence;
            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            start += breakAt;
            // Inside a fence only the break character goes: leading whitespace is code indentation.
            if (fenceAfter is null)
                start = SkipWhitespace(content, start);
            else if (start < content.Length && content[start] is '\n' or ' ')
                start++;
            openFence = fenceAfter;
        }

        return chunks;
    }

    private static int BreakPoint(string content, int start, int budget)
    {
        var window = content.AsSpan(start, budget);
        var floor = budget / MinBreakFraction;

        var breakAt = window.LastIndexOf('\n');
        if (breakAt < floor)
            breakAt = window.LastIndexOf(' ');
        if (breakAt >= floor && breakAt > 0)
            return breakAt;

        // A lone surrogate renders as U+FFFD on both sides of the cut.
        return char.IsHighSurrogate(window[budget - 1]) ? budget - 1 : budget;
    }

    // The opener ("```sql") of the block still open at the end of `text`, or null.
    private static string? FenceAfter(string text, string? openFence)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            // Discord closes a block at a ``` that ends a content line ("...}```"), as on its own line.
            if (openFence is not null)
            {
                if (trimmed.EndsWith(Fence, StringComparison.Ordinal))
                    openFence = null;
            }
            // A one-line block (```json {}```) opens and closes on the same line.
            else if (trimmed.StartsWith(Fence, StringComparison.Ordinal)
                && trimmed.IndexOf(Fence, Fence.Length, StringComparison.Ordinal) < 0)
                openFence = Opener(trimmed);
        }

        return openFence;
    }

    private static string Opener(string fenceLine)
    {
        var tag = fenceLine[Fence.Length..].Split(' ', 2)[0];
        return tag.Length <= MaxFenceTagLength ? Fence + tag : Fence;
    }

    private static int SkipWhitespace(string content, int start)
    {
        while (start < content.Length && char.IsWhiteSpace(content[start]))
            start++;
        return start;
    }
}
