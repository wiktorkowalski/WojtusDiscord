using System.Text;
using Microsoft.Extensions.AI;

namespace DiscordEventService.Services.Conversation;

// Collects the web-search citations of a round and renders the turn's compact source list
// (#271, #426). The Claude API puts `web_search_result_location` citations on text blocks;
// in a stream each arrives as a `citations_delta`, which the MEAI adapter turns into an
// empty TextContent whose Annotations hold one CitationAnnotation (Url, Title).
internal static class WebSearchCitations
{
    internal sealed record Citation(string Url, string? Title);

    // Collects the round's citations into `citations`, deduplicating by URL across the
    // whole turn (a later round may re-search what an earlier round already cited).
    public static void AccumulateRound(IEnumerable<ChatResponseUpdate> updates, List<Citation> citations)
    {
        var annotations = updates
            .SelectMany(update => update.Contents)
            .OfType<TextContent>()
            .SelectMany(text => text.Annotations ?? [])
            .OfType<CitationAnnotation>();

        foreach (var annotation in annotations)
        {
            if (annotation.Url is not { IsAbsoluteUri: true } url)
                continue;

            var urlValue = url.OriginalString;
            if (!citations.Any(existing => existing.Url.Equals(urlValue, StringComparison.Ordinal)))
                citations.Add(new Citation(urlValue, annotation.Title));
        }
    }

    // One subtext line of domain-named markdown links (the model already inlines
    // domain-named links in its own text; this is the structured source list under the
    // reply). Repeated domains get an ordinal so two articles stay distinguishable.
    public static string? FormatSourceList(IReadOnlyList<Citation> citations)
    {
        if (citations.Count == 0)
            return null;

        var line = new StringBuilder("-# 🔗 ");
        var domainCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < citations.Count; i++)
        {
            var domain = DomainOf(citations[i].Url);
            var count = domainCounts[domain] = domainCounts.GetValueOrDefault(domain) + 1;
            if (i > 0)
                line.Append(" · ");
            line.Append('[').Append(domain);
            if (count > 1)
                line.Append(" (").Append(count).Append(')');
            line.Append("](").Append(citations[i].Url).Append(')');
        }

        return line.ToString();
    }

    private static string DomainOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return url;

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
    }
}
