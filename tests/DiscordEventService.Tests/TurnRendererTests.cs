using DiscordEventService.Services.Conversation.Interaction;
using Xunit;

namespace DiscordEventService.Tests;

// The discrete render contract (#274), now reachable end-to-end over the interaction port
// (#308) instead of only its pure ComposeRound: deltas buffer silently, a tool round posts
// exactly one message, the answer posts complete at the end, a blank buffer posts nothing,
// and a failed round's partial deltas are dropped rather than appended to the retry.
public sealed class TurnRendererTests
{
    [Fact]
    public void ComposeRound_NarrationPresent_SummaryBecomesClosingSubtextLine()
    {
        var composed = TurnRenderer.ComposeRound("Sprawdzam memy. ", "-# 🔧 meme_search");

        Assert.Equal("Sprawdzam memy.\n-# 🔧 meme_search", composed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void ComposeRound_NoVisibleNarration_SummaryStandsAlone(string narration)
    {
        Assert.Equal("-# 🔧 meme_search", TurnRenderer.ComposeRound(narration, "-# 🔧 meme_search"));
    }

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
    public async Task CompleteRound_PostsNarrationWithSummary_AndRetriggersTyping()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);
        await renderer.AppendDeltaAsync("Zaraz sprawdzę.");

        await renderer.CompleteRoundAsync("-# 🔧 meme_search");

        Assert.Equal(["Zaraz sprawdzę.\n-# 🔧 meme_search"], surface.Sent);
        // The round's post cleared Discord's typing indicator, so it is re-triggered for
        // the next round's model call, which is already in flight.
        Assert.Equal(2, surface.TypingCount);
    }

    [Fact]
    public async Task RoundBoundary_FlushesTheBuffer_SoTheAnswerIsOnlyTheLastRoundsDeltas()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync("Sprawdzam.");
        await renderer.CompleteRoundAsync("-# 🔧 meme_search");
        await renderer.AppendDeltaAsync("Znalazłem trzy.");
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Sprawdzam.\n-# 🔧 meme_search", "Znalazłem trzy."], surface.Sent);
        Assert.Equal(2, renderer.MessageCount);
    }

    [Fact]
    public async Task ResetRound_DropsThePartialDeltas_SoTheRetryRendersOnce()
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface);

        await renderer.AppendDeltaAsync("Zna");
        renderer.ResetRound();
        await renderer.AppendDeltaAsync("Znalazłem trzy.");
        await renderer.CompleteTurnAsync();

        Assert.Equal(["Znalazłem trzy."], surface.Sent);
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
