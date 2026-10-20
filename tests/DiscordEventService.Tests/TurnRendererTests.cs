using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// The discrete render contract (#274, #438), end-to-end over the interaction port (#308):
// deltas buffer silently, the narration before a tool call posts as one message and every
// call as its own message, the answer posts complete at the end, a blank buffer posts
// nothing, and a failed round's partial deltas are dropped rather than appended to the retry.
public sealed class TurnRendererTests
{
    [Fact]
    public async Task Deltas_BufferUntilTheTurnEnds_ThenPostAsOneMessage()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);

        renderer.AppendDelta("Jasne, ");
        renderer.AppendDelta("już patrzę.");
        Assert.Empty(surface.Sent);

        await renderer.CompleteTurnAsync();

        Assert.Equal(["Jasne, już patrzę."], surface.Sent);
        Assert.Equal(1, renderer.MessageCount);
    }

    [Fact]
    public async Task ToolCalls_PostNarrationThenEachCall_WithNoTypingPostPerMessage()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);
        renderer.AppendDelta("Zaraz sprawdzę.");

        await renderer.PostToolCallAsync("-# 🔧 meme_search");
        await renderer.PostToolCallAsync("-# 🔧 meme_search");

        // Two calls of one tool are two messages: no Distinct by name.
        Assert.Equal(["Zaraz sprawdzę.", "-# 🔧 meme_search", "-# 🔧 meme_search"], surface.Sent);
        // #456: no typing POST per message; the timer brings it back after the burst.
        Assert.Equal(0, surface.TypingCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task ToolCall_NoVisibleNarration_PostsOnlyTheCalls(string narration)
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);
        renderer.AppendDelta(narration);

        await renderer.PostToolCallAsync("-# 🔧 guild_stats");

        Assert.Equal(["-# 🔧 guild_stats"], surface.Sent);
    }

    [Fact]
    public async Task RoundBoundary_FlushesTheBuffer_SoTheAnswerIsOnlyTheLastRoundsDeltas()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);

        renderer.AppendDelta("Sprawdzam.");
        await renderer.PostToolCallAsync("-# 🔧 meme_search");
        renderer.AppendDelta("Znalazłem trzy.");
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Sprawdzam.", "-# 🔧 meme_search", "Znalazłem trzy."], surface.Sent);
        Assert.Equal(3, renderer.MessageCount);
    }

    [Fact]
    public async Task SearchCall_SplitsTheTextBeforeAndAfterTheSearch()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);

        renderer.AppendDelta("Okej, szukam kursu…");
        await renderer.PostToolCallAsync("-# 🔎 web_search: kurs EUR/PLN");
        renderer.AppendDelta("Dziś 4,25 zł.");
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Okej, szukam kursu…", "-# 🔎 web_search: kurs EUR/PLN", "Dziś 4,25 zł."], surface.Sent);
    }

    [Fact]
    public async Task ResetRound_DropsThePartialDeltas_SoTheRetryRendersOnce()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);

        renderer.AppendDelta("Szukam… ");
        renderer.AppendDelta("Zna");
        renderer.ResetRound("Zna".Length);
        renderer.AppendDelta("Znalazłem trzy.");
        await renderer.CompleteTurnAsync();

        // Only the failed attempt's text goes; the paused text before it stays.
        Assert.Equal(["Szukam… Znalazłem trzy."], surface.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task BlankBuffer_PostsNothing(string content)
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);

        renderer.AppendDelta(content);
        await renderer.CompleteTurnAsync();

        Assert.Empty(surface.Sent);
        Assert.Equal(0, renderer.MessageCount);
    }

    [Fact]
    public async Task AnswerPastTheMessageCap_IsChunkedAndEachChunkCounts()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);

        renderer.AppendDelta(new string('x', 4000));
        await renderer.CompleteTurnAsync();

        Assert.Equal(3, surface.Sent.Count);
        Assert.Equal(3, renderer.MessageCount);
        Assert.All(surface.Sent, chunk => Assert.True(chunk.Length <= 1950));
        Assert.Equal(4000, surface.Sent.Sum(chunk => chunk.Length));
    }

    // #456 at test scale (refresh 80 ms for 8 s): a round that streams no text — thinking,
    // a slow tool, retry backoff — still refreshes the indicator, and it stops with the turn.
    [Fact]
    public async Task SilentRound_KeepsTypingAlive_UntilTheTurnCompletes()
    {
        var surface = new FakeTurnSurface();
        await using var renderer = new TurnRenderer(surface, NullLogger.Instance, TimeSpan.FromMilliseconds(80));

        renderer.StartTyping();
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await renderer.CompleteTurnAsync();

        // The first typing at once, then at least two refreshes in a ~3-window silence.
        var typingAtEnd = surface.TypingCount;
        Assert.InRange(typingAtEnd, 3, 5);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Equal(typingAtEnd, surface.TypingCount);
    }

    [Fact]
    public async Task ToolRoundBurst_RetriggersTypingOnce_AfterTheLastLine()
    {
        var surface = new FakeTurnSurface();
        // Ticks every 50 ms; the 400 ms keep-alive never fires inside this test.
        await using var renderer = new TurnRenderer(surface, NullLogger.Instance, TimeSpan.FromMilliseconds(400));
        renderer.StartTyping();
        renderer.AppendDelta("Sprawdzam.");

        await renderer.PostToolCallAsync("-# 🔧 meme_search");
        await renderer.PostToolCallAsync("-# 🔧 guild_stats");
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Sprawdzam.", "-# 🔧 meme_search", "-# 🔧 guild_stats"], surface.Sent);
        // The start, then one typing for the whole burst of three sends.
        Assert.Equal(2, surface.TypingCount);
    }
}
