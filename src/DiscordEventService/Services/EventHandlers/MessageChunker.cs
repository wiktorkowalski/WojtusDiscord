namespace DiscordEventService.Services.EventHandlers;

// Splits a reply into Discord-sized chunks on a newline or word boundary so nothing is
// dropped mid-word. Shared by the streaming conversation renderer (#241) and any one-shot
// reply. Pure and side-effect free so the boundary logic is unit-testable.
internal static class MessageChunker
{
    public static List<string> Chunk(string content, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<string> chunks = [];
        var start = 0;

        while (content.Length - start > limit)
        {
            var window = content.Substring(start, limit);
            var breakAt = window.LastIndexOf('\n');
            if (breakAt <= 0)
                breakAt = window.LastIndexOf(' ');
            if (breakAt <= 0)
                breakAt = limit;

            chunks.Add(content.Substring(start, breakAt).TrimEnd());
            start += breakAt;
            while (start < content.Length && char.IsWhiteSpace(content[start]))
                start++;
        }

        if (start < content.Length)
            chunks.Add(content[start..]);

        return chunks;
    }
}
