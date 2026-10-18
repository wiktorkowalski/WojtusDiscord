using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.AI;
using Xunit;

namespace DiscordEventService.Tests;

// #447: a long source list used to be one line; the chunker split it at a space and the
// next message lost the "-# " prefix, so the links rendered full size.
public sealed class WebSearchCitationsTests
{
    [Fact]
    public void FormatSourceList_ManyLongUrls_EveryChunkStartsAsSubtext()
    {
        var citations = Enumerable.Range(1, 20)
            .Select(i => new WebSearchCitations.Citation($"https://news{i}.example/{new string('a', 180)}/{i}", null))
            .ToList();

        var sources = WebSearchCitations.FormatSourceList(citations)!;
        var chunks = MessageChunker.Chunk("Odpowiedź.\n" + sources, 1950);

        Assert.All(chunks.Skip(1), chunk => Assert.StartsWith("-# ", chunk));
        Assert.All(sources.Split('\n'), line => Assert.StartsWith("-# 🔗 ", line));
        Assert.All(citations, citation => Assert.Contains(citation.Url, sources));
    }

    [Fact]
    public void Annotations_SkipNonHttpUrls()
    {
        var text = new TextContent("")
        {
            Annotations =
            [
                new CitationAnnotation { Url = new Uri("javascript:alert(1)") },
                new CitationAnnotation { Url = new Uri("https://ok.example/a") },
            ],
        };

        var urls = WebSearchCitations.Annotations([text]).Select(citation => citation.Url);

        Assert.Equal(["https://ok.example/a"], urls);
    }

    [Fact]
    public void FormatSourceList_OnlyOverlongUrls_ReturnsNull()
    {
        Assert.Null(WebSearchCitations.FormatSourceList([new($"https://x.example/{new string('a', 1000)}", null)]));
    }
}
