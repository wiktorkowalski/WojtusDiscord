using System.Net;
using Anthropic.Exceptions;
using Anthropic.Models;
using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Xunit;

namespace DiscordEventService.Tests;

// The pure seams of the §2 retry policy (#268, #426): transient classification of the
// Anthropic SDK failures and the backoff shape. The end-to-end paths — the mid-stream error
// event, Retry-After honouring, ledger rows — run through the real SDK in ConversationRetryTests.
public sealed class ConversationRetryPolicyTests
{
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(529)]
    public void IsTransient_RetryableStatus(int status) =>
        Assert.True(ConversationRetryPolicy.IsTransient(ApiException(status)));

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(413)]
    public void IsTransient_ClientErrorStatus_IsTerminal(int status) =>
        Assert.False(ConversationRetryPolicy.IsTransient(ApiException(status)));

    [Fact]
    public void IsTransient_MidStreamAndTransportFaults_AreRetryable()
    {
        Assert.True(ConversationRetryPolicy.IsTransient(new AnthropicIOException("I/O exception", new HttpRequestException("reset"))));
        Assert.True(ConversationRetryPolicy.IsTransient(new HttpRequestException("reset")));
        Assert.True(ConversationRetryPolicy.IsTransient(new IOException("broken pipe")));
        // The SDK's attempt timeout, and any other cancellation: the round loop rethrows the
        // turn's own cancellation first, so what reaches the policy is never the caller's.
        Assert.True(ConversationRetryPolicy.IsTransient(
            new TaskCanceledException("client timeout", new TimeoutException("elapsed"))));
        Assert.True(ConversationRetryPolicy.IsTransient(new TaskCanceledException("linked cancel")));
        Assert.True(ConversationRetryPolicy.IsTransient(new OperationCanceledException()));
    }

    // An `error` event in the stream retries only the types that can pass on their own (#457).
    [Theory]
    [InlineData(ErrorType.OverloadedError)]
    [InlineData(ErrorType.ApiError)]
    [InlineData(ErrorType.RateLimitError)]
    [InlineData(ErrorType.TimeoutError)]
    public void IsTransient_SseErrorOfTransientType_IsRetryable(ErrorType type) =>
        Assert.True(ConversationRetryPolicy.IsTransient(new AnthropicSseException("SSE error") { ErrorType = type }));

    // null: a type this SDK version does not know, e.g. request_too_large.
    [Theory]
    [InlineData(ErrorType.InvalidRequestError)]
    [InlineData(ErrorType.AuthenticationError)]
    [InlineData(ErrorType.PermissionError)]
    [InlineData(ErrorType.NotFoundError)]
    [InlineData(ErrorType.BillingError)]
    [InlineData(null)]
    public void IsTransient_SseErrorOfOtherType_IsTerminal(ErrorType? type) =>
        Assert.False(ConversationRetryPolicy.IsTransient(new AnthropicSseException("SSE error") { ErrorType = type }));

    [Fact]
    public void IsTransient_UnexpectedExceptions_AreTerminal()
    {
        Assert.False(ConversationRetryPolicy.IsTransient(new InvalidOperationException("bug")));
        Assert.False(ConversationRetryPolicy.IsTransient(new AnthropicInvalidDataException("bad json")));
    }

    [Fact]
    public void ComputeDelay_FullJitter_StaysWithinExponentialCeiling()
    {
        var options = new ConversationOptions { RetryBaseDelayMs = 1000, RetryMaxDelayMs = 8000 };

        for (var seed = 0; seed < 50; seed++)
        {
            var random = new Random(seed);
            AssertWithin(ConversationRetryPolicy.ComputeDelay(1, options, random), 1000);
            AssertWithin(ConversationRetryPolicy.ComputeDelay(2, options, random), 2000);
            AssertWithin(ConversationRetryPolicy.ComputeDelay(3, options, random), 4000);
            // Past the exponential curve the cap wins.
            AssertWithin(ConversationRetryPolicy.ComputeDelay(10, options, random), 8000);
        }
    }

    [Fact]
    public void ComputeDelay_RetryAfter_WinsWhenLonger_JitterWhenShorter()
    {
        var options = new ConversationOptions { RetryBaseDelayMs = 1, RetryMaxDelayMs = 2 };

        Assert.Equal(TimeSpan.FromSeconds(3),
            ConversationRetryPolicy.ComputeDelay(1, options, new Random(1), TimeSpan.FromSeconds(3)));
        AssertWithin(ConversationRetryPolicy.ComputeDelay(1, new ConversationOptions(), new Random(1),
            TimeSpan.FromTicks(1)), 1000);
    }

    [Fact]
    public void ParseRetryAfter_PrefersMilliseconds_IgnoresJunk()
    {
        using var both = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        both.Headers.TryAddWithoutValidation("retry-after-ms", "1500");
        both.Headers.TryAddWithoutValidation("retry-after", "9");
        Assert.Equal(TimeSpan.FromMilliseconds(1500), AnthropicAttemptLogHandler.ParseRetryAfter(both));

        using var seconds = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        seconds.Headers.TryAddWithoutValidation("retry-after", "2");
        Assert.Equal(TimeSpan.FromSeconds(2), AnthropicAttemptLogHandler.ParseRetryAfter(seconds));

        using var junk = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        junk.Headers.TryAddWithoutValidation("retry-after", "soon");
        Assert.Null(AnthropicAttemptLogHandler.ParseRetryAfter(junk));
    }

    // #487: the API's x-should-retry outweighs the status rule; it says nothing about the
    // stream and transport faults.
    [Theory]
    [InlineData(500, false, false)]
    [InlineData(409, true, true)]
    [InlineData(400, null, false)]
    [InlineData(529, null, true)]
    public void IsTransient_ShouldRetryHeaderWinsOverStatus(int status, bool? shouldRetry, bool expected) =>
        Assert.Equal(expected, ConversationRetryPolicy.IsTransient(ApiException(status), shouldRetry));

    [Fact]
    public void IsTransient_ShouldRetryFalse_LeavesTransportFaultsRetryable() =>
        Assert.True(ConversationRetryPolicy.IsTransient(new HttpRequestException("reset"), shouldRetry: false));

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("maybe", null)]
    [InlineData(null, null)]
    public void ParseShouldRetry_ReadsTrueOrFalse_IgnoresJunk(string? header, bool? expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        if (header is not null)
            response.Headers.TryAddWithoutValidation("x-should-retry", header);
        Assert.Equal(expected, AnthropicAttemptLogHandler.ParseShouldRetry(response));
    }

    private static AnthropicApiException ApiException(int status) =>
        AnthropicExceptionFactory.CreateApiException((HttpStatusCode)status, "{}");

    private static void AssertWithin(TimeSpan delay, int ceilingMs) =>
        Assert.InRange(delay.TotalMilliseconds, 0, ceilingMs);
}
