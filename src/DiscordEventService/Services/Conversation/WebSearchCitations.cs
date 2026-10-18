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
        foreach (var (url, annotation) in Annotations(updates.SelectMany(update => update.Contents)))
        {
            if (!citations.Any(existing => existing.Url.Equals(url, StringComparison.Ordinal)))
                citations.Add(new Citation(url, annotation.Title));
        }
    }

    // The citations among the contents, with an http(s) URL, in order.
    public static IEnumerable<(string Url, CitationAnnotation Annotation)> Annotations(IEnumerable<AIContent> contents) =>
        contents.OfType<TextContent>()
            .SelectMany(text => text.Annotations ?? [])
            .OfType<CitationAnnotation>()
            .Where(annotation => annotation.Url is { IsAbsoluteUri: true } url && IsHttpUrl(url.OriginalString))
            .Select(annotation => (annotation.Url!.OriginalString, annotation));

    // Only http(s): the URL is replayed and rendered verbatim, so it must not carry a newline or another scheme.
    internal static bool IsHttpUrl(string url) =>
        url.Length <= 2048
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '[' or ']');

    private const string LinePrefix = "-# 🔗 ";

    // About half a Discord message at most: every MessageChunker window then holds a line
    // break past its first quarter, so no chunk starts mid-line without "-# " (#447).
    private const int MaxLineLength = 1000;

    // Leaves room for the prefix and the "[domain (n)]()" around the URL on one line.
    private const int MaxUrlLength = 900;

    // Subtext lines of domain-named markdown links (the model already inlines
    // domain-named links in its own text; this is the structured source list under the
    // reply). Repeated domains get an ordinal so two articles stay distinguishable.
    public static string? FormatSourceList(IReadOnlyList<Citation> citations)
    {
        List<string> lines = [];
        List<string> links = [];
        var domainCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var citation in citations.Where(citation => citation.Url.Length <= MaxUrlLength))
        {
            var domain = DomainOf(citation.Url);
            var count = domainCounts[domain] = domainCounts.GetValueOrDefault(domain) + 1;
            var link = $"[{domain}{(count > 1 ? $" ({count})" : "")}]({citation.Url})";
            if (links.Count > 0 && LineOf(links).Length + " · ".Length + link.Length > MaxLineLength)
            {
                lines.Add(LineOf(links));
                links.Clear();
            }
            links.Add(link);
        }

        if (links.Count > 0)
            lines.Add(LineOf(links));
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static string LineOf(List<string> links) => LinePrefix + string.Join(" · ", links);

    private static string DomainOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return url;

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
    }
}
