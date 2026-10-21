using Anthropic.Exceptions;
using Anthropic.Models;
using DiscordEventService.Configuration;

namespace DiscordEventService.Services.Conversation;

// Failure classification + backoff for the §2 per-round retry policy (#268, #426). Pure
// functions only — the retry loop in ConversationService owns the state, the ledger writes
// and the actual delay. It is the only retry layer: the SDK runs with MaxRetries 0.
internal static class ConversationRetryPolicy
{
    // Transient = worth re-issuing the same round:
    // - an API error response the API marks x-should-retry: true, or with no such header a
    //   status of 408, 429 or ≥500 (529 is "overloaded"). The header wins both ways (#487);
    // - an `error` event in the middle of the stream (AnthropicSseException) of a type that can
    //   pass on its own: overloaded, api, rate limit or timeout. Any other type, or one this SDK
    //   version does not know (null, e.g. request_too_large), fails the same way again (#457);
    // - a transport fault: the SDK wraps a pre-response one in AnthropicIOException; a fault
    //   while the body streams surfaces raw as HttpRequestException or IOException;
    // - any cancellation: the SDK attempt timeout (ClientOptions.Timeout, response headers
    //   only) throws TaskCanceledException wrapping TimeoutException, but the shape is not
    //   relied on. The round loop rethrows the turn's own cancellation before classifying,
    //   so a cancellation that reaches this is never the caller's.
    // Everything else (400/401/403/404, unexpected bugs) is terminal: retrying cannot help —
    // surface the visible failure line instead.
    public static bool IsTransient(Exception failure, bool? shouldRetry = null) => failure switch
    {
        AnthropicApiException api => shouldRetry ?? IsTransientStatus((int)api.StatusCode),
        AnthropicSseException sse => sse.ErrorType
            is ErrorType.OverloadedError or ErrorType.ApiError or ErrorType.RateLimitError or ErrorType.TimeoutError,
        AnthropicIOException => true,
        HttpRequestException or IOException => true,
        OperationCanceledException => true,
        _ => false,
    };

    // The int, not the enum: 529 (overloaded) has no HttpStatusCode name.
    public static bool IsTransientStatus(int status) => status is 408 or 429 or >= 500;

    // Exponential backoff with full jitter: random(0, base·2^(attempt-1)) capped at
    // RetryMaxDelayMs. The Retry-After the API sent (captured by AnthropicAttemptLogHandler)
    // wins when it asks for longer. A wait past the turn's RequestTimeoutSeconds budget is not
    // taken: the round loop ends the round as failed instead (#445).
    public static TimeSpan ComputeDelay(
        int attempt, ConversationOptions options, Random random, TimeSpan? retryAfter = null)
    {
        var ceilingMs = (int)Math.Min(
            (long)options.RetryBaseDelayMs << (attempt - 1), options.RetryMaxDelayMs);
        var delay = TimeSpan.FromMilliseconds(random.Next(0, ceilingMs + 1));

        return retryAfter is { } wait && wait > delay ? wait : delay;
    }
}
