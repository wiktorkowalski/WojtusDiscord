using System.Diagnostics;
using System.Globalization;

namespace DiscordEventService.Services.Conversation;

// One model-call attempt of the loop, as the HTTP handler below sees it (#426). The loop
// starts a scope right before it opens the stream; the request is sent from that execution
// context, so the handler finds the scope and writes into it. The SDK exceptions carry no
// response headers: this is how the retry policy gets the Retry-After of a 429 or 529.
internal sealed class AnthropicAttemptScope
{
    private static readonly AsyncLocal<AnthropicAttemptScope?> CurrentScope = new();

    private AnthropicAttemptScope(int round, int attempt)
    {
        Round = round;
        Attempt = attempt;
    }

    public static AnthropicAttemptScope? Current => CurrentScope.Value;

    public int Round { get; }
    public int Attempt { get; }

    // The wait the API asked for on a failed response, from retry-after-ms or retry-after.
    public TimeSpan? RetryAfter { get; set; }

    public static AnthropicAttemptScope Begin(int round, int attempt)
    {
        var scope = new AnthropicAttemptScope(round, attempt);
        CurrentScope.Value = scope;
        return scope;
    }
}

// Sits between the SDK and its HttpClient (ClientOptions.Handlers), so it sees every attempt.
// The SDK retry is off (MaxRetries 0): the loop's ConversationRetryPolicy owns the retries and
// logs "retrying in N ms"; this line adds what only the HTTP layer knows — the status, the
// request id and the Retry-After. It reads status and headers only; the SDK reads the body.
internal sealed class AnthropicAttemptLogHandler(ILogger logger) : DelegatingHandler
{
    private const string RequestIdHeader = "request-id";
    private const string RetryAfterMsHeader = "retry-after-ms";
    private const string RetryAfterHeader = "retry-after";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = AnthropicAttemptScope.Current;
        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // The message only: the loop logs the exception with its stack once.
            logger.LogWarning(
                "Claude API round {Round} attempt {Attempt} got no response after {ElapsedMs} ms: {Error}",
                scope?.Round, scope?.Attempt, ElapsedMs(started), ex.Message);
            throw;
        }

        // The int, not the enum: 529 (overloaded) has no HttpStatusCode name.
        var status = (int)response.StatusCode;
        if (status is 408 or 409 or 429 or >= 500)
        {
            var retryAfter = ParseRetryAfter(response);
            if (scope is not null)
                scope.RetryAfter = retryAfter;

            logger.LogWarning(
                "Claude API round {Round} attempt {Attempt} failed with status {Status} after {ElapsedMs} ms, "
                + "request id {RequestId}, retry-after {RetryAfterMs} ms",
                scope?.Round, scope?.Attempt, status, ElapsedMs(started),
                Header(response, RequestIdHeader) ?? "none",
                retryAfter is { } wait ? (long)wait.TotalMilliseconds : null);
        }

        return response;
    }

    // retry-after-ms wins (the API's precise value); retry-after is in seconds. Unparsable or
    // non-positive values count as absent.
    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (Header(response, RetryAfterMsHeader) is { } ms
            && double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds)
            && milliseconds > 0)
            return TimeSpan.FromMilliseconds(milliseconds);

        if (Header(response, RetryAfterHeader) is { } s
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
            return TimeSpan.FromSeconds(seconds);

        return null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
