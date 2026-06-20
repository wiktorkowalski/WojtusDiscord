using DiscordEventService.Services.EventHandlers;
using Xunit;

namespace DiscordEventService.Tests;

// The streaming renderer (#241) spills a long answer into several Discord messages; the
// chunker is the load-bearing boundary logic, so pin its behaviour: nothing dropped, no
// chunk over the limit, and a clean break preferred over a mid-word cut.
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
}
