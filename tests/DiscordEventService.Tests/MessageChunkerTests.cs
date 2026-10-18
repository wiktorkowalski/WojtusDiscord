using DiscordEventService.Services.Conversation.Interaction;
using Xunit;

namespace DiscordEventService.Tests;

// TurnRenderer spills a long answer into several Discord messages; the chunker is the
// load-bearing boundary logic, so pin its behaviour: nothing dropped, no chunk over the
// limit, a clean break preferred over a mid-word cut, and code blocks kept whole (#447).
public sealed class MessageChunkerTests
{
    [Fact]
    public void Chunk_ShortContent_ReturnsSingleChunk()
    {
        var chunks = MessageChunker.Chunk("hello world", 1950);

        Assert.Equal(["hello world"], chunks);
    }

    [Fact]
    public void Chunk_AtLimit_ReturnsSingleChunk()
    {
        var content = new string('a', 50);

        var chunks = MessageChunker.Chunk(content, 50);

        Assert.Equal([content], chunks);
    }

    [Fact]
    public void Chunk_PrefersNewlineBoundary_AndDropsNothing()
    {
        var first = new string('a', 40);
        var second = new string('b', 40);
        var content = $"{first}\n{second}";

        var chunks = MessageChunker.Chunk(content, 50);

        Assert.Equal([first, second], chunks);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 50));
    }

    [Fact]
    public void Chunk_NoBreakpoint_HardSplitsAtLimit()
    {
        var content = new string('a', 120);

        var chunks = MessageChunker.Chunk(content, 50);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 50));
        Assert.Equal(content, string.Concat(chunks));
    }

    [Fact]
    public void Chunk_BreaksOnSpace_WhenNoNewline()
    {
        var content = "word1 word2 word3 word4 word5 word6 word7 word8";

        var chunks = MessageChunker.Chunk(content, 20);

        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 20));
        // Word boundaries preserved: reassembling with single spaces restores the text.
        Assert.Equal(content, string.Join(' ', chunks));
    }

    // #445: a leading newline used to break at 1 and emit an empty chunk, which Discord rejects.
    [Fact]
    public void Chunk_LeadingNewlines_EmitNoEmptyChunk_AndDropNothing()
    {
        var body = new string('a', 2000);

        var chunks = MessageChunker.Chunk("\n\n" + body, 1950);

        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk)));
        Assert.Equal(body, string.Concat(chunks));
    }

    // #447: a cut inside a ``` block closes and reopens the fence, language tag included.
    [Fact]
    public void Chunk_FencedBlockAcrossLimit_KeepsEveryChunkFenced()
    {
        var lines = Enumerable.Range(1, 120).Select(i => $"    SELECT count(*) FROM t{i} WHERE x = {i};");
        var content = "Zapytanie:\n```sql\n" + string.Join('\n', lines) + "\n```\nKoniec.";

        var chunks = MessageChunker.Chunk(content, 1950);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            Assert.True(chunk.Length <= 1950);
            Assert.Equal(0, CountFences(chunk) % 2);
        });
        Assert.All(chunks.Skip(1), chunk => Assert.StartsWith("```sql\n", chunk));
        // Indentation inside the fence survives every split.
        Assert.All(chunks.Skip(1), chunk => Assert.StartsWith("    SELECT", chunk.Split('\n')[1]));
        var code = chunks.SelectMany(chunk => chunk.Split('\n')).Where(line => line.StartsWith("    SELECT"));
        Assert.Equal(lines, code);
    }

    [Fact]
    public void Chunk_EmojiAtLimit_NeverEndsInHighSurrogate()
    {
        var content = string.Concat(Enumerable.Repeat("😀", 100));

        var chunks = MessageChunker.Chunk(content, 51);

        Assert.All(chunks, chunk => Assert.False(char.IsHighSurrogate(chunk[^1])));
        Assert.Equal(content, string.Concat(chunks));
    }

    [Fact]
    public void Chunk_EarlyNewline_DoesNotEmitTinyFirstChunk()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("słowo", 400));

        var chunks = MessageChunker.Chunk("Header\n" + paragraph, 1950);

        Assert.StartsWith("Header\nsłowo", chunks[0]);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 1950));
    }

    [Fact]
    public void Chunk_OneLineBlock_DoesNotFlipFenceParity()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("słowo", 600));

        var chunks = MessageChunker.Chunk("```json {\"a\":1}```\n" + paragraph, 1950);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.DoesNotContain("\n```", chunk));
        Assert.All(chunks.Skip(1), chunk => Assert.StartsWith("słowo", chunk));
    }

    private static int CountFences(string chunk) =>
        chunk.Split('\n').Count(line => line.Trim().StartsWith("```", StringComparison.Ordinal));
}
