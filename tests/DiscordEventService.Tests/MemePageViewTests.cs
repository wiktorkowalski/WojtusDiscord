using DiscordEventService.Services.MemeIndexing;
using DSharpPlus.Entities;
using Xunit;

namespace DiscordEventService.Tests;

// The /meme page as text and buttons (#391), and the custom id that carries a button's state.
public sealed class MemePageViewTests
{
    private const ulong GuildId = 10UL;

    [Theory]
    [InlineData(0, "geralt")]
    [InlineData(35, "kot: lodówka: 3:15")]
    [InlineData(5, ":")]
    [InlineData(5, "")]
    public void CustomId_BuildThenParse_ReturnsTheOffsetAndTheQuery(int offset, string query)
    {
        var id = MemePageCustomId.Build(offset, query);

        Assert.True(MemePageCustomId.TryParse(id, out var parsedOffset, out var parsedQuery));
        Assert.Equal(offset, parsedOffset);
        Assert.Equal(query, parsedQuery);
    }

    [Fact]
    public void CustomId_CanPage_HoldsUpToTheQueryThatFitsWithTheLongestOffset()
    {
        // "meme-page:" is 10 characters, the longest offset 10, the colon 1: 79 are left for the query.
        var longestQuery = new string('a', 79);

        Assert.True(MemePageCustomId.CanPage(longestQuery));
        Assert.Equal(MemePageCustomId.MaxLength, MemePageCustomId.Build(int.MaxValue, longestQuery).Length);
        Assert.False(MemePageCustomId.CanPage(longestQuery + "a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("conv6:confirm:abc")]
    [InlineData("meme-page")]
    [InlineData("meme-page:5")]
    [InlineData("meme-page:x:geralt")]
    [InlineData("meme-page:-5:geralt")]
    [InlineData("meme-page::geralt")]
    public void CustomId_TryParse_RejectsWhatIsNotAPageButton(string? customId)
    {
        Assert.False(MemePageCustomId.TryParse(customId, out _, out _));
    }

    [Fact]
    public void HitLabel_PolishDescription_IsItsFirstSentence()
    {
        var hit = Hit(descriptionPl: "Kot siedzi w lodówce obok mleka. Patrzy na ser.", descriptionEn: "A cat in a fridge.", tags: ["kot"]);

        Assert.Equal("Kot siedzi w lodówce obok mleka.", MemePageView.HitLabel(hit));
    }

    [Fact]
    public void HitLabel_AbbreviationEarlyInTheText_DoesNotEndTheLabel()
    {
        var hit = Hit(descriptionPl: "Mem z np. kotem na lodówce. Obok stoi mleko.");

        Assert.Equal("Mem z np. kotem na lodówce.", MemePageView.HitLabel(hit));
    }

    [Fact]
    public void HitLabel_FirstSentenceUnderTheMinimum_RunsToTheNextSentenceEnd()
    {
        var hit = Hit(descriptionPl: "Kot w lodówce. Obok stoi mleko. Dalej jest ser.");

        Assert.Equal("Kot w lodówce. Obok stoi mleko.", MemePageView.HitLabel(hit));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void HitLabel_NoPolishDescription_FallsBackToEnglish(string? descriptionPl)
    {
        var hit = Hit(descriptionPl, descriptionEn: "A cat sits inside a fridge. Milk next to it.");

        Assert.Equal("A cat sits inside a fridge.", MemePageView.HitLabel(hit));
    }

    [Fact]
    public void HitLabel_NoDescription_FallsBackToTheFileName()
    {
        Assert.Equal("kot.png", MemePageView.HitLabel(Hit(descriptionPl: null, descriptionEn: " ", fileName: "kot.png")));
    }

    [Fact]
    public void HitLabel_LineEndings_BecomeSpaces()
    {
        Assert.Equal("Kot siedzi w lodówce", MemePageView.HitLabel(Hit(descriptionPl: "Kot siedzi\r\nw lodówce")));
    }

    [Fact]
    public void HitLabel_LongSentence_IsCutWithAnEllipsis()
    {
        var label = MemePageView.HitLabel(Hit(descriptionPl: new string('k', 300)));

        Assert.Equal(MemePageView.MaxLabelLength, label.Length);
        Assert.EndsWith("…", label);
    }

    [Fact]
    public void Render_FirstPageOfMany_HasTheHeaderTheHitLinesAndOnlyNext()
    {
        var view = MemePageView.Render(GuildId, "geralt", offset: 0, Page(hitCount: 5, total: 87));

        var lines = view.Content.Split('\n');
        Assert.Equal("1–5 z 87", lines[0]);
        Assert.Equal(6, lines.Length);
        Assert.Equal("https://discord.com/channels/10/20/1000 Opis mema numer 0 z kotem w lodówce.", lines[1]);

        var next = Assert.Single(view.Buttons);
        Assert.Equal(MemePageView.NextLabel, next.Label);
        Assert.Equal("meme-page:5:geralt", next.CustomId);
    }

    [Fact]
    public void Render_MiddlePage_HasPreviousAndNext()
    {
        var view = MemePageView.Render(GuildId, "geralt", offset: 5, Page(hitCount: 5, total: 87));

        Assert.StartsWith("6–10 z 87\n", view.Content);
        Assert.Equal([MemePageView.PreviousLabel, MemePageView.NextLabel], view.Buttons.Select(b => b.Label));
        Assert.Equal(["meme-page:0:geralt", "meme-page:10:geralt"], view.Buttons.Select(b => b.CustomId));
    }

    [Fact]
    public void Render_LastPage_HasOnlyPrevious()
    {
        var view = MemePageView.Render(GuildId, "geralt", offset: 85, Page(hitCount: 2, total: 87));

        Assert.StartsWith("86–87 z 87\n", view.Content);
        var previous = Assert.Single(view.Buttons);
        Assert.Equal(MemePageView.PreviousLabel, previous.Label);
        Assert.Equal("meme-page:80:geralt", previous.CustomId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Render_SinglePage_HasNoButtons(int total)
    {
        var view = MemePageView.Render(GuildId, "geralt", offset: 0, Page(hitCount: total, total));

        Assert.StartsWith($"1–{total} z {total}\n", view.Content);
        Assert.Empty(view.Buttons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Render_QueryTooLongForACustomId_ShowsThePageWithoutButtons(int offset)
    {
        var view = MemePageView.Render(GuildId, new string('a', 80), offset, Page(hitCount: 5, total: 87));

        Assert.StartsWith($"{offset + 1}–{offset + 5} z 87\n", view.Content);
        Assert.Empty(view.Buttons);
    }

    // The id grows with the digits of the offset: a query that has buttons on page 1 has them on every page.
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Render_LongestQueryThatCanBePaged_KeepsBothButtonsAtEveryOffset(int offset)
    {
        var query = new string('a', 79);

        var view = MemePageView.Render(GuildId, query, offset, Page(hitCount: 5, total: 5000));

        Assert.Equal([MemePageView.PreviousLabel, MemePageView.NextLabel], view.Buttons.Select(b => b.Label));
        Assert.Equal(
            [$"meme-page:{offset - 5}:{query}", $"meme-page:{offset + 5}:{query}"],
            view.Buttons.Select(b => b.CustomId));
    }

    [Fact]
    public void Render_NoHits_SaysSoWithoutButtons()
    {
        var view = MemePageView.Render(GuildId, "geralt", offset: 0, new MemeSearchPage([], 0));

        Assert.Equal("Nic nie znalazłem dla tego zapytania.", view.Content);
        Assert.Empty(view.Buttons);
    }

    [Fact]
    public void Render_PagePastTheEnd_KeepsTheWayBack()
    {
        var view = MemePageView.Render(GuildId, "geralt", offset: 10, new MemeSearchPage([], 0));

        var previous = Assert.Single(view.Buttons);
        Assert.Equal("meme-page:5:geralt", previous.CustomId);
    }

    [Fact]
    public void Render_LongestIdsAndLabels_StaysInsideTheMessageLimit()
    {
        var hits = Enumerable.Range(0, MemePageView.PageSize)
            .Select(_ => Hit(descriptionPl: new string('k', 300), channelId: ulong.MaxValue, messageId: ulong.MaxValue))
            .ToList();

        var view = MemePageView.Render(ulong.MaxValue, "geralt", offset: 2_000_000_000, new MemeSearchPage(hits, int.MaxValue));

        Assert.True(view.Content.Length < 2000, $"content is {view.Content.Length} characters");
    }

    [Fact]
    public void ApplyTo_Builder_CarriesTheTextWithMentionsDisarmed()
    {
        var hit = Hit(descriptionPl: "@everyone patrzy na kota.");
        var view = MemePageView.Render(GuildId, "geralt", offset: 0, new MemeSearchPage([hit], 87));

        var builder = view.ApplyTo(new DiscordMessageBuilder());

        Assert.Equal(view.Content, builder.Content);
        Assert.Contains("@everyone", builder.Content);
        // Non-null and empty is allowed_mentions.parse: [] — see InteractionMentionSuppressionTests.
        Assert.NotNull(builder.Mentions);
        Assert.Empty(builder.Mentions);
        Assert.NotEmpty(builder.Components);
    }

    private static MemeSearchPage Page(int hitCount, int total) =>
        new(Enumerable.Range(0, hitCount)
            .Select(i => Hit(descriptionPl: $"Opis mema numer {i} z kotem w lodówce. Reszta opisu.", messageId: 1000UL + (ulong)i))
            .ToList(), total);

    private static MemeSearchHit Hit(
        string? descriptionPl,
        string? descriptionEn = null,
        string fileName = "meme.png",
        string[]? tags = null,
        ulong channelId = 20UL,
        ulong messageId = 1000UL) =>
        new(channelId, messageId, AttachmentDiscordId: messageId + 1, fileName, descriptionPl, descriptionEn,
            tags ?? [], DateTime.UnixEpoch, Score: 1.0);
}
