using DiscordEventService.Services.Conversation.Interaction;
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
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync("Jasne, ");
        await renderer.AppendDeltaAsync("już patrzę.");
        Assert.Empty(surface.Sent);

        await renderer.CompleteTurnAsync();

        Assert.Equal(["Jasne, już patrzę."], surface.Sent);
        Assert.Equal(1, renderer.MessageCount);
    }

    [Fact]
    public async Task ToolCalls_PostNarrationThenEachCall_AndRetriggersTypingAfterEach()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);
        await renderer.AppendDeltaAsync("Zaraz sprawdzę.");

        await renderer.PostToolCallAsync("-# 🔧 meme_search");
        await renderer.PostToolCallAsync("-# 🔧 meme_search");

        // Two calls of one tool are two messages: no Distinct by name.
        Assert.Equal(["Zaraz sprawdzę.", "-# 🔧 meme_search", "-# 🔧 meme_search"], surface.Sent);
        // Every post cleared Discord's typing indicator, so it is re-triggered after each:
        // once for the first delta, then once per message.
        Assert.Equal(4, surface.TypingCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task ToolCall_NoVisibleNarration_PostsOnlyTheCalls(string narration)
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);
        await renderer.AppendDeltaAsync(narration);

        await renderer.PostToolCallAsync("-# 🔧 guild_stats");

        Assert.Equal(["-# 🔧 guild_stats"], surface.Sent);
    }

    [Fact]
    public async Task RoundBoundary_FlushesTheBuffer_SoTheAnswerIsOnlyTheLastRoundsDeltas()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync("Sprawdzam.");
        await renderer.PostToolCallAsync("-# 🔧 meme_search");
        await renderer.AppendDeltaAsync("Znalazłem trzy.");
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Sprawdzam.", "-# 🔧 meme_search", "Znalazłem trzy."], surface.Sent);
        Assert.Equal(3, renderer.MessageCount);
    }

    [Fact]
    public async Task SearchCall_SplitsTheTextBeforeAndAfterTheSearch()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync("Okej, szukam kursu…");
        await renderer.PostToolCallAsync("-# 🔎 web_search: kurs EUR/PLN");
        await renderer.AppendDeltaAsync("Dziś 4,25 zł.");
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Okej, szukam kursu…", "-# 🔎 web_search: kurs EUR/PLN", "Dziś 4,25 zł."], surface.Sent);
        Assert.Equal(3, surface.TypingCount);
    }

    [Fact]
    public async Task ResetRound_DropsThePartialDeltas_SoTheRetryRendersOnce()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync("Szukam… ");
        await renderer.AppendDeltaAsync("Zna");
        renderer.ResetRound("Zna".Length);
        await renderer.AppendDeltaAsync("Znalazłem trzy.");
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
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync(content);
        await renderer.CompleteTurnAsync();

        Assert.Empty(surface.Sent);
        Assert.Equal(0, renderer.MessageCount);
    }

    [Fact]
    public async Task AnswerPastTheMessageCap_IsChunkedAndEachChunkCounts()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync(new string('x', 4000));
        await renderer.CompleteTurnAsync();

        Assert.Equal(3, surface.Sent.Count);
        Assert.Equal(3, renderer.MessageCount);
        Assert.All(surface.Sent, chunk => Assert.True(chunk.Length <= 1950));
        Assert.Equal(4000, surface.Sent.Sum(chunk => chunk.Length));
    }

    [Fact]
    public async Task TypingRefresh_IsThrottled_WhileDeltasStream()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        // The first delta triggers typing; the rest fall inside the ~8s refresh window.
        for (var i = 0; i < 20; i++)
            await renderer.AppendDeltaAsync("x");

        Assert.Equal(1, surface.TypingCount);
    }
}
