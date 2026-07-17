using DiscordEventService.Services.EventHandlers;
using Xunit;

namespace DiscordEventService.Tests;

// The pure seam of the discrete renderer (#274): how a tool round's buffered narration
// and its summary line compose into the round's standalone message. The Discord I/O
// around it (posting, typing) is live-verified on the dev bot.
public sealed class DiscordTurnRendererTests
{
    [Fact]
    public void ComposeRound_NarrationPresent_SummaryBecomesClosingSubtextLine()
    {
        var composed = DiscordTurnRenderer.ComposeRound("Sprawdzam memy. ", "-# 🔧 meme_search");

        Assert.Equal("Sprawdzam memy.\n-# 🔧 meme_search", composed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void ComposeRound_NoVisibleNarration_SummaryStandsAlone(string narration)
    {
        Assert.Equal("-# 🔧 meme_search", DiscordTurnRenderer.ComposeRound(narration, "-# 🔧 meme_search"));
    }
}
