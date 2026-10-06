using DiscordEventService.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// A collector that is down fails every batch. Each failure is counted; the log gets one
// Warning per interval, with the number of failures it stands for.
public sealed class TraceExportFailureListenerTests
{
    [Fact]
    public void RecordFailure_WarnsOncePerIntervalAndCountsWhatItSkipped()
    {
        var log = new RecordingLogger();
        var clock = new ManualClock();
        using var listener = new TraceExportFailureListener(log.For<TraceExportFailureListener>(), clock);

        listener.RecordFailure("FailedToReachCollector", ["http://tempo:4318/v1/traces"]);
        listener.RecordFailure("FailedToReachCollector", ["http://tempo:4318/v1/traces"]);
        clock.Advance(TraceExportFailureListener.WarningInterval - TimeSpan.FromSeconds(1));
        listener.RecordFailure("FailedToReachCollector", ["http://tempo:4318/v1/traces"]);

        var first = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, first.Level);
        Assert.StartsWith("Trace export failed 1 time(s) since the last warning (FailedToReachCollector): http://tempo:4318/v1/traces.", first.Message);

        clock.Advance(TimeSpan.FromSeconds(1));
        listener.RecordFailure("FailedToReachCollector", ["http://tempo:4318/v1/traces"]);

        Assert.Equal(2, log.Entries.Count);
        Assert.StartsWith("Trace export failed 3 time(s) since the last warning", log.Entries[1].Message);
    }

    [Fact]
    public void DescribePayload_KeepsTheFirstLineOfEachPart()
    {
        object?[] payload =
        [
            "http://tempo:4318/v1/traces",
            "System.Net.Http.HttpRequestException: Connection refused (tempo:4318)\n ---> System.Net.Sockets.SocketException\n   at X.Y()",
        ];

        Assert.Equal(
            "http://tempo:4318/v1/traces; System.Net.Http.HttpRequestException: Connection refused (tempo:4318)",
            TraceExportFailureListener.DescribePayload(payload));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
