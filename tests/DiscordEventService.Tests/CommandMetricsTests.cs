using DiscordEventService.Commands;
using DiscordEventService.Infrastructure;
using Xunit;

namespace DiscordEventService.Tests;

// The slash command metric contract: one count and one duration per execution, and an
// outcome from a fixed set (never the exception message).
public sealed class CommandMetricsTests
{
    [Fact]
    public void CommandFinished_CountsOneExecutionWithItsDuration()
    {
        var command = $"test-{Guid.NewGuid():N}";
        using var metrics = new MetricsCapture();

        BotMetrics.CommandFinished(command, CommandMetrics.Classify(null), TimeSpan.FromMilliseconds(250));

        var counted = Assert.Single(metrics.Of("wojtus.command.executions", "command", command));
        Assert.Equal("ok", counted.Tags["outcome"]);
        Assert.Equal(1, counted.Value);
        Assert.Equal(0.25, Assert.Single(metrics.Of("wojtus.command.duration", "command", command)).Value);
    }

    [Fact]
    public void Classify_MapsAnExceptionToAFixedOutcome()
    {
        Assert.Equal("ok", CommandMetrics.Classify(null));
        Assert.Equal("failed", CommandMetrics.Classify(new InvalidOperationException("free text that must not be a label")));
        Assert.Equal("cancelled", CommandMetrics.Classify(new TaskCanceledException()));
    }
}
