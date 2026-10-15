using System.Collections.Concurrent;
using Anthropic.Models.Messages;
using DiscordEventService.Configuration;
using Microsoft.Extensions.AI;

namespace DiscordEventService.Services.Conversation;

// The native usage of one model call (#426), from the UsageContent the MEAI adapter emits at
// the end of a stream. A stream that died after message_start keeps its input counts (from
// the raw event); one that died before it has none: every field null.
//   Input       total input = uncached + cache read + cache write (the adapter's sum)
//   CacheRead   cache_read_input_tokens (UsageDetails.CachedInputTokenCount)
//   CacheWrite  cache_creation_input_tokens (AdditionalCounts, absent when 0)
//   WebSearches server_tool_use.web_search_requests (AdditionalCounts, absent when 0)
internal sealed record RoundTokens(int? Input, int? Output, int? CacheRead, int? CacheWrite, int? WebSearches)
{
    public static readonly RoundTokens None = new(null, null, null, null, null);

    internal const string CacheWriteKey = "CacheCreationInputTokens";
    private const string WebSearchesKey = "WebSearchRequests";

    public static RoundTokens From(IReadOnlyList<ChatResponseUpdate> updates)
    {
        var usage = updates
            .SelectMany(update => update.Contents)
            .OfType<UsageContent>()
            .LastOrDefault();
        if (usage is null)
            return FromMessageStart(updates);

        var details = usage.Details;
        var hasInput = details.InputTokenCount is not null;
        return new RoundTokens(
            ToInt(details.InputTokenCount),
            ToInt(details.OutputTokenCount),
            // A reported usage with no cache counts means 0, not unknown.
            hasInput ? ToInt(details.CachedInputTokenCount) ?? 0 : null,
            hasInput ? ToInt(Additional(details, CacheWriteKey)) ?? 0 : null,
            ToInt(Additional(details, WebSearchesKey)));
    }

    // The adapter emits UsageContent only when a stream ends normally. A stream that died after
    // message_start has still billed its input: read it from the raw message_start event. The
    // output count is unknown then (message_start reports a placeholder), so it stays null.
    private static RoundTokens FromMessageStart(IEnumerable<ChatResponseUpdate> updates)
    {
        var start = updates
            .Select(update => update.RawRepresentation)
            .OfType<RawMessageStreamEvent>()
            .Select(raw => raw.TryPickStart(out var started) ? started.Message.Usage : null)
            .FirstOrDefault(found => found is not null);
        if (start is null)
            return None;

        var cacheRead = start.CacheReadInputTokens ?? 0;
        var cacheWrite = start.CacheCreationInputTokens ?? 0;
        return new RoundTokens(
            ToInt(start.InputTokens + cacheRead + cacheWrite), null, ToInt(cacheRead), ToInt(cacheWrite), null);
    }

    private static long? Additional(UsageDetails details, string key) =>
        details.AdditionalCounts?.TryGetValue(key, out var value) is true ? value : null;

    private static int? ToInt(long? value) => value is { } v ? (int)Math.Clamp(v, 0, int.MaxValue) : null;
}

// The USD cost of one model call (#426): the Claude API reports tokens, not money.
//   cost = uncached × input + read × cache read + write × cache write + output × output
//          + searches × fee per search
// TokensUsd is the same without the search fee: the ledger keeps it as
// upstream_inference_cost_usd, so cost − upstream stays the search fee (#271).
internal sealed record RoundCost(double TotalUsd, double TokensUsd);

internal static class ConversationCost
{
    private const double PerMillion = 1_000_000d;

    // One Warning per model id per process: a missing price is a config gap, not per-call news.
    private static readonly ConcurrentDictionary<string, byte> WarnedModels = new(StringComparer.Ordinal);

    // Null when the call reported no usage (it died mid-stream) or the model has no price.
    public static RoundCost? Compute(ConversationOptions options, RoundTokens tokens, ILogger logger)
    {
        if (tokens.Input is not { } input)
            return null;

        if (!options.Prices.TryGetValue(options.Model, out var price))
        {
            if (WarnedModels.TryAdd(options.Model, 0))
                logger.LogWarning(
                    "No price for model {Model} in Conversation:Prices; its conversation_usage rows get no cost",
                    options.Model);
            return null;
        }

        var cacheRead = tokens.CacheRead ?? 0;
        var cacheWrite = tokens.CacheWrite ?? 0;
        var uncached = Math.Max(0, input - cacheRead - cacheWrite);

        var tokensUsd =
            (uncached * price.InputPerMTokUsd
             + cacheRead * price.CacheReadPerMTokUsd
             + cacheWrite * price.CacheWritePerMTokUsd
             + (tokens.Output ?? 0) * price.OutputPerMTokUsd) / PerMillion;
        var searchUsd = (tokens.WebSearches ?? 0) * options.WebSearchPerThousandUsd / 1000d;

        return new RoundCost(tokensUsd + searchUsd, tokensUsd);
    }
}
