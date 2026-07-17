using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Xunit;

namespace DiscordEventService.Tests;

// The pure seams of the §2 retry policy (#268): transient classification, the
// unknown-finish-reason normalization (B-OQ1's throwing surface), and backoff shape.
// The end-to-end paths — both mid-stream surfaces, Retry-After honoring, ledger rows —
// run through the real SDK adapter in ConversationRetryTests.
public sealed class ConversationRetryPolicyTests
{
    [Fact]
    public void IsTransient_MidStreamAndTransportFaults_AreRetryable()
    {
        Assert.True(ConversationRetryPolicy.IsTransient(new MidStreamErrorException("frame")));
        Assert.True(ConversationRetryPolicy.IsTransient(new HttpRequestException("reset")));
        Assert.True(ConversationRetryPolicy.IsTransient(new IOException("broken pipe")));
        // An HTTP-stack timeout, not the turn's own token — the round loop rethrows the
        // turn cancellation before classification ever sees it.
        Assert.True(ConversationRetryPolicy.IsTransient(new TaskCanceledException("timeout")));
    }

    [Fact]
    public void IsTransient_UnexpectedExceptions_AreTerminal()
    {
        Assert.False(ConversationRetryPolicy.IsTransient(new InvalidOperationException("bug")));
        // Un-normalized: only AsRoundFailure's wrapping makes the finish-reason surface transient.
        Assert.False(ConversationRetryPolicy.IsTransient(
            new ArgumentOutOfRangeException("value", "error", "Unknown ChatFinishReason value.")));
    }

    [Fact]
    public void AsRoundFailure_UnknownChatFinishReason_NormalizesToMidStreamError()
    {
        // The SDK's actual surface for OpenRouter's finish_reason:"error" frame (probed, B-OQ1).
        var thrown = new ArgumentOutOfRangeException("value", "error", "Unknown ChatFinishReason value.");

        var normalized = ConversationRetryPolicy.AsRoundFailure(thrown);

        var midStream = Assert.IsType<MidStreamErrorException>(normalized);
        Assert.Same(thrown, midStream.InnerException);
        Assert.True(ConversationRetryPolicy.IsTransient(normalized));
    }

    [Fact]
    public void AsRoundFailure_OtherExceptions_PassThroughUnchanged()
    {
        var thrown = new InvalidOperationException("bug");
        Assert.Same(thrown, ConversationRetryPolicy.AsRoundFailure(thrown));

        var unrelatedRange = new ArgumentOutOfRangeException("count", 5, "out of range");
        Assert.Same(unrelatedRange, ConversationRetryPolicy.AsRoundFailure(unrelatedRange));
    }

    [Fact]
    public void ComputeDelay_FullJitter_StaysWithinExponentialCeiling()
    {
        var options = new ConversationOptions { RetryBaseDelayMs = 1000, RetryMaxDelayMs = 8000 };
        var failure = new HttpRequestException("reset");

        for (var seed = 0; seed < 50; seed++)
        {
            var random = new Random(seed);
            AssertWithin(ConversationRetryPolicy.ComputeDelay(1, failure, options, random), 1000);
            AssertWithin(ConversationRetryPolicy.ComputeDelay(2, failure, options, random), 2000);
            AssertWithin(ConversationRetryPolicy.ComputeDelay(3, failure, options, random), 4000);
            // Past the exponential curve the cap wins.
            AssertWithin(ConversationRetryPolicy.ComputeDelay(10, failure, options, random), 8000);
        }
    }

    private static void AssertWithin(TimeSpan delay, int ceilingMs)
    {
        Assert.InRange(delay.TotalMilliseconds, 0, ceilingMs);
    }
}
