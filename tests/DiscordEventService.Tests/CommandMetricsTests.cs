using System.Runtime.CompilerServices;
using DiscordEventService.Commands;
using DiscordEventService.Infrastructure;
using DSharpPlus.Commands.Processors.TextCommands;
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

    // A command that caught its own exception: the extension reports it as executed, and the
    // mark turns that into "failed", the value the WojtusCommandErrors rule matches. A context
    // cannot be built outside the extension, so this is one without its constructor: it has no
    // command, and the name falls back to "unknown".
    [Fact]
    public async Task RecordAsync_ContextMarkedFailed_CountsFailedAndAnotherContextStaysOk()
    {
        var marked = (TextCommandContext)RuntimeHelpers.GetUninitializedObject(typeof(TextCommandContext));
        var unmarked = (TextCommandContext)RuntimeHelpers.GetUninitializedObject(typeof(TextCommandContext));
        using var metrics = new MetricsCapture();

        CommandMetrics.MarkFailed(marked);
        await CommandMetrics.RecordAsync(marked, exception: null);
        await CommandMetrics.RecordAsync(unmarked, exception: null);

        var outcomes = metrics.Of("wojtus.command.executions", "command", "unknown").Select(m => m.Tags["outcome"]).ToList();
        Assert.Equal(["failed", "ok"], outcomes);
    }

    [Fact]
    public void Classify_MapsAnExceptionToAFixedOutcome()
    {
        Assert.Equal("ok", CommandMetrics.Classify(null));
        Assert.Equal("failed", CommandMetrics.Classify(new InvalidOperationException("free text that must not be a label")));
        Assert.Equal("cancelled", CommandMetrics.Classify(new TaskCanceledException()));
    }
}
