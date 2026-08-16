using DiscordEventService.Services;
using Xunit;

namespace DiscordEventService.Tests;

// The #320 state machine: a streak of failed heartbeat writes with the gateway up becomes
// one DbUnreachable window, buffered in memory because the DB cannot hold state mid-outage.
public sealed class UnwritableWindowTrackerTests
{
    private static readonly DateTime T0 = new(2026, 7, 25, 0, 39, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    private readonly UnwritableWindowTracker _tracker = new(Threshold);

    [Fact]
    public void ASingleTransientFailure_IsDiscarded_AndTheStateClears()
    {
        _tracker.OnWriteFailed(T0, gatewayConnected: true);

        Assert.Null(_tracker.OnWriteSucceeded(T0.AddSeconds(5)));
        Assert.False(_tracker.HasPendingWindow);
    }

    [Fact]
    public void AStreakPastTheThreshold_BecomesOneWindow_FromFirstFailureToRecovery()
    {
        // The 2026-07-25 shape: minutes of failed writes while the gateway stayed up.
        for (var tick = 0; tick < 96; tick++)
            _tracker.OnWriteFailed(T0.AddSeconds(tick * 5), gatewayConnected: true);

        var window = _tracker.OnWriteSucceeded(T0.AddMinutes(8));

        Assert.NotNull(window);
        Assert.Equal(T0, window.StartedAtUtc);
        Assert.Equal(T0.AddMinutes(8), window.EndedAtUtc);
        Assert.Equal(96, window.FailedWriteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void AFailureWithTheGatewayDownOrUnknown_OpensNoWindow(bool? gatewayConnected)
    {
        // With the gateway down the process is in a full outage the gateway-disconnect
        // and startup-gap paths already own.
        _tracker.OnWriteFailed(T0, gatewayConnected);

        Assert.False(_tracker.HasPendingWindow);
        Assert.Null(_tracker.OnWriteSucceeded(T0.AddMinutes(5)));
    }

    [Fact]
    public void AGatewayDropMidWindow_DoesNotSplitTheWindow()
    {
        _tracker.OnWriteFailed(T0, gatewayConnected: true);
        _tracker.OnWriteFailed(T0.AddSeconds(5), gatewayConnected: false);
        _tracker.OnWriteFailed(T0.AddSeconds(10), gatewayConnected: true);

        var window = _tracker.OnWriteSucceeded(T0.AddSeconds(35));

        Assert.NotNull(window);
        Assert.Equal(3, window.FailedWriteCount);
    }

    [Fact]
    public void ARecordableWindow_StaysPendingUntilReset_SoAFailedPersistRetries()
    {
        _tracker.OnWriteFailed(T0, gatewayConnected: true);

        Assert.NotNull(_tracker.OnWriteSucceeded(T0.AddSeconds(35)));
        // The caller could not commit the row — the window must survive for the next tick.
        Assert.True(_tracker.HasPendingWindow);
        Assert.NotNull(_tracker.OnWriteSucceeded(T0.AddSeconds(40)));

        _tracker.Reset();
        Assert.False(_tracker.HasPendingWindow);
        Assert.Null(_tracker.OnWriteSucceeded(T0.AddSeconds(45)));
    }

    [Fact]
    public void ARetryAfterAFailedPersist_DoesNotSlideTheRecoveryInstantForward()
    {
        _tracker.OnWriteFailed(T0, gatewayConnected: true);

        var first = _tracker.OnWriteSucceeded(T0.AddSeconds(35));
        // The persist failed; the retry a minute later must report the SAME window — the
        // DB was writable in between and the heartbeat rows prove it.
        var retry = _tracker.OnWriteSucceeded(T0.AddSeconds(95));

        Assert.Equal(first!.EndedAtUtc, retry!.EndedAtUtc);
        Assert.Equal(T0.AddSeconds(35), retry.EndedAtUtc);
    }

    [Fact]
    public void ANewFailureWhileAPersistIsPending_ReopensTheWindow()
    {
        _tracker.OnWriteFailed(T0, gatewayConnected: true);
        Assert.NotNull(_tracker.OnWriteSucceeded(T0.AddSeconds(35)));

        // The DB went down again before the row could be committed — the merged window
        // must extend to the new recovery, not stay pinned at the first one.
        _tracker.OnWriteFailed(T0.AddSeconds(40), gatewayConnected: true);
        var window = _tracker.OnWriteSucceeded(T0.AddSeconds(80));

        Assert.Equal(T0, window!.StartedAtUtc);
        Assert.Equal(T0.AddSeconds(80), window.EndedAtUtc);
        Assert.Equal(2, window.FailedWriteCount);
    }

    [Fact]
    public void ASuccessWithNothingPending_ReturnsNull()
    {
        Assert.Null(_tracker.OnWriteSucceeded(T0));
    }
}
