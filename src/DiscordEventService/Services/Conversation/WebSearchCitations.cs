using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace DiscordEventService.Services.Conversation;

// Recovers `url_citation` annotations from a round's raw streamed chunks and renders the
// turn's compact source list (#271). OpenRouter spreads the annotations across mid-stream
// delta chunks (one per citation as the model writes, `start_index`/`end_index` always 0)
// at `choices[0].delta.annotations`. The OpenAI SDK has no typed annotation surface on
// streamed deltas, and the chunk root's JsonPatch can't reach them either (they are
// retained inside the NESTED delta model's patch — the root patch stays empty), so the
// recovery round-trips each chunk through ModelReaderWriter and reads the JSON.
internal static class WebSearchCitations
{
    internal sealed record Citation(string Url, string Title);

    // Collects the round's citations into `citations`, deduplicating by URL across the
    // whole turn (a later round may re-search what an earlier round already cited).
    public static void AccumulateRound(IEnumerable<ChatResponseUpdate> updates, List<Citation> citations)
    {
        foreach (var update in updates)
        {
            if (update.RawRepresentation is not StreamingChatCompletionUpdate raw)
                continue;

            using var chunk = JsonDocument.Parse(ModelReaderWriter.Write(raw));
            if (!chunk.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind is not JsonValueKind.Array)
                continue;

            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta)
                    || !delta.TryGetProperty("annotations", out var annotations)
                    || annotations.ValueKind is not JsonValueKind.Array)
                    continue;

                foreach (var annotation in annotations.EnumerateArray())
                {
                    if (!annotation.TryGetProperty("url_citation", out var citation)
                        || !citation.TryGetProperty("url", out var url)
                        || url.GetString() is not { Length: > 0 } urlValue)
                        continue;

                    if (!citations.Any(existing => existing.Url.Equals(urlValue, StringComparison.Ordinal)))
                    {
                        citations.Add(new Citation(urlValue,
                            citation.TryGetProperty("title", out var title) ? title.GetString() ?? "" : ""));
                    }
                }
            }
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
