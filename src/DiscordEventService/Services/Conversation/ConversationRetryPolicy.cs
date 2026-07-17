using System.ClientModel;
using System.Globalization;
using DiscordEventService.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace DiscordEventService.Services.Conversation;

// OpenRouter delivers a post-header failure as a normal SSE data chunk (top-level
// `error`, usually with `finish_reason:"error"`) — never as an HTTP failure. Probed
// against the real SDK (#268, research B-OQ1): a frame carrying finish_reason "error"
// makes the OpenAI SDK throw ArgumentOutOfRangeException("Unknown ChatFinishReason
// value") out of the enumeration, while a frame without it passes through silently as
// an empty update whose raw patch carries `$.error` (which would misfire the
// empty-answer fallback). Both surfaces normalize to this exception so the per-round
// retry handles them as one transient failure mode.
internal sealed class MidStreamErrorException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// Failure classification + backoff for the §2 per-round retry policy (#268). Pure
// functions only — the retry loop in ConversationService owns the state, the ledger
// writes and the actual delay.
internal static class ConversationRetryPolicy
{
    // Transient = worth re-issuing the same round: the meme client's 408/429/≥500
    // convention (OpenRouterClient), transport faults, and the mid-stream error frame.
    // TaskCanceledException here is an HTTP-stack timeout — the round loop rethrows the
    // turn's own cancellation before classifying, so it never reaches this. Status 0 is
    // a ClientResultException wrapping a transport fault. Everything else (400/401/402/
    // 403, unexpected bugs) is terminal: retrying cannot help — surface the visible
    // failure line instead.
    public static bool IsTransient(Exception failure) => failure switch
    {
        MidStreamErrorException => true,
        ClientResultException clientResult => clientResult.Status is 0 or 408 or 429 or >= 500,
        HttpRequestException or IOException or TaskCanceledException => true,
        _ => false,
    };

    // The silent mid-stream surface: the error frame deserialized as a normal update,
    // with the unmapped top-level `error` object left on the raw chunk's JSON patch.
    public static bool IsMidStreamErrorFrame(ChatResponseUpdate update)
    {
#pragma warning disable SCME0001 // JsonPatch on the raw chunk is experimental.
        return update.RawRepresentation is StreamingChatCompletionUpdate raw
            && raw.Patch.Contains("$.error"u8);
#pragma warning restore SCME0001
    }

    // The throwing mid-stream surface: the SDK rejects the frame's nonstandard
    // finish_reason "error" before the chunk (and its $.error payload) materializes.
    public static Exception AsRoundFailure(Exception thrown) =>
        thrown is ArgumentOutOfRangeException
        && thrown.Message.Contains("ChatFinishReason", StringComparison.Ordinal)
            ? new MidStreamErrorException("mid-stream error frame (finish_reason \"error\")", thrown)
            : thrown;

    // Exponential backoff with full jitter: random(0, base·2^(attempt-1)) capped at
    // RetryMaxDelayMs, per OpenRouter's own guidance. A 429's Retry-After header wins
    // when it asks for longer — the turn's RequestTimeoutSeconds budget still bounds
    // the wait (Task.Delay runs under the turn token).
    public static TimeSpan ComputeDelay(
        int attempt, Exception failure, ConversationOptions options, Random random)
    {
        var ceilingMs = (int)Math.Min(
            (long)options.RetryBaseDelayMs << (attempt - 1), options.RetryMaxDelayMs);
        var delay = TimeSpan.FromMilliseconds(random.Next(0, ceilingMs + 1));

        return TryGetRetryAfter(failure, out var retryAfter) && retryAfter > delay
            ? retryAfter
            : delay;
    }

    // OpenRouter sends Retry-After in seconds; ignore unparsable or non-positive values.
    private static bool TryGetRetryAfter(Exception failure, out TimeSpan retryAfter)
    {
        retryAfter = default;
        if (failure is not ClientResultException clientResult)
            return false;

        var headers = clientResult.GetRawResponse()?.Headers;
        if (headers is null || !headers.TryGetValue("Retry-After", out var value))
            return false;

        if (!double.TryParse(value, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            return false;

        retryAfter = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
