using System.Diagnostics;
using System.Runtime.CompilerServices;
using DiscordEventService.Commands;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.EventHandlers;
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

    // A press of a /meme paging button: counted with the command instruments under its own
    // command name, so a failed press is visible where a failed command is.
    [Fact]
    public void RecordInteraction_CountsThePressWithItsOutcomeAndTheTimeSinceTheInteraction()
    {
        var command = $"press-{Guid.NewGuid():N}";
        using var metrics = new MetricsCapture();

        CommandMetrics.RecordInteraction(command, failed: false, DateTimeOffset.UtcNow.AddSeconds(-2));
        CommandMetrics.RecordInteraction(command, failed: true, DateTimeOffset.UtcNow.AddSeconds(-2));
        // A clock behind Discord's must not give a negative time.
        CommandMetrics.RecordInteraction(command, failed: false, DateTimeOffset.UtcNow.AddMinutes(1));

        var outcomes = metrics.Of("wojtus.command.executions", "command", command).Select(m => m.Tags["outcome"]).ToList();
        Assert.Equal(["ok", "failed", "ok"], outcomes);
        var durations = metrics.Of("wojtus.command.duration", "command", command).Select(m => m.Value).ToList();
        Assert.InRange(durations[0], 2, 30);
        Assert.InRange(durations[1], 2, 30);
        Assert.Equal(0, durations[2]);
    }

    // The span of a press is a root Tempo keeps, with the database span under it, and a failed
    // press marks it.
    [Fact]
    public void StartSpan_ByName_IsARootThatKeepsItsDatabaseSpanAndCarriesTheFailure()
    {
        using var npgsql = new ActivitySource("Npgsql");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CommandMetrics.SourceName || ReferenceEquals(source, npgsql),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;

        using var span = CommandMetrics.StartSpan(MemePageComponentHandler.MetricCommand);

        Assert.NotNull(span);
        Assert.Equal("command meme_page", span.DisplayName);
        Assert.True(TelemetryRegistration.IsForTempo(span));
        using (var sql = npgsql.StartActivity("select"))
        {
            Assert.Equal(span.SpanId, sql!.ParentSpanId);
            Assert.True(TelemetryRegistration.IsForTempo(sql));
        }

        CommandMetrics.RecordInteraction($"press-{Guid.NewGuid():N}", failed: true, DateTimeOffset.UtcNow);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public void Classify_MapsAnExceptionToAFixedOutcome()
    {
        Assert.Equal("ok", CommandMetrics.Classify(null));
        Assert.Equal("failed", CommandMetrics.Classify(new InvalidOperationException("free text that must not be a label")));
        Assert.Equal("cancelled", CommandMetrics.Classify(new TaskCanceledException()));
    }
}
