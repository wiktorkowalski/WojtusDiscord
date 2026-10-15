using Anthropic.Exceptions;
using DiscordEventService.Configuration;

namespace DiscordEventService.Services.Conversation;

// Failure classification + backoff for the §2 per-round retry policy (#268, #426). Pure
// functions only — the retry loop in ConversationService owns the state, the ledger writes
// and the actual delay. It is the only retry layer: the SDK runs with MaxRetries 0.
internal static class ConversationRetryPolicy
{
    // Transient = worth re-issuing the same round:
    // - an API status of 408, 429 or ≥500 (529 is "overloaded");
    // - an `error` event in the middle of the stream (AnthropicSseException, e.g. overloaded_error);
    // - a transport fault: the SDK wraps a pre-response one in AnthropicIOException; a fault
    //   while the body streams surfaces raw as HttpRequestException or IOException;
    // - an attempt timeout: the SDK throws TaskCanceledException wrapping TimeoutException.
    //   The round loop rethrows the turn's own cancellation before classifying, so a
    //   cancellation that reaches this is never the caller's.
    // Everything else (400/401/403/404, unexpected bugs) is terminal: retrying cannot help —
    // surface the visible failure line instead.
    public static bool IsTransient(Exception failure) => failure switch
    {
        AnthropicApiException api => (int)api.StatusCode is 408 or 429 or >= 500,
        AnthropicSseException or AnthropicIOException => true,
        HttpRequestException or IOException => true,
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false,
    };

    // Exponential backoff with full jitter: random(0, base·2^(attempt-1)) capped at
    // RetryMaxDelayMs. The Retry-After the API sent (captured by AnthropicAttemptLogHandler)
    // wins when it asks for longer — the turn's RequestTimeoutSeconds budget still bounds the
    // wait (Task.Delay runs under the turn token).
    public static TimeSpan ComputeDelay(
        int attempt, ConversationOptions options, Random random, TimeSpan? retryAfter = null)
    {
        var ceilingMs = (int)Math.Min(
            (long)options.RetryBaseDelayMs << (attempt - 1), options.RetryMaxDelayMs);
        var delay = TimeSpan.FromMilliseconds(random.Next(0, ceilingMs + 1));

        return retryAfter is { } wait && wait > delay ? wait : delay;
    }
}
