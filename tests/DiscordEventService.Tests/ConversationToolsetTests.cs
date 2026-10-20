using System.Diagnostics;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// The dispatch seam's load-bearing guarantee (#240): a tool that fails — whether it
// throws or simply doesn't exist — must come back to the model as an error string,
// never as an exception out of the agentic loop.
public sealed class ConversationToolsetTests
{
    [Fact]
    public async Task InvokeAsync_ThrowingTool_ReturnsErrorStringWithoutThrowing()
    {
        var boom = AIFunctionFactory.Create(
            (Func<string>)(() => throw new InvalidOperationException("kaboom")),
            new AIFunctionFactoryOptions { Name = "boom", Description = "always throws" });
        var toolset = new ConversationToolset([boom], NullLogger.Instance);

        var result = await toolset.InvokeAsync(
            new FunctionCallContent("call_1", "boom", new Dictionary<string, object?>()),
            CancellationToken.None);

        Assert.Equal("call_1", result.CallId);
        Assert.Contains("Error running tool", result.Result?.ToString());
    }

    // query_database hands a failed query to the model as text. The model must get that text
    // unchanged, and the call must not be counted as "ok".
    [Fact]
    public async Task InvokeAsync_ToolThrowsSoftFailure_ReturnsItsTextAndCountsAnError()
    {
        var name = $"soft_{Guid.NewGuid():N}";
        var log = new RecordingLogger();
        var tool = AIFunctionFactory.Create(
            (Func<string>)(() => throw new ToolSoftFailureException("SQL error [42601]: syntax error")),
            new AIFunctionFactoryOptions { Name = name, Description = "fails softly" });
        var toolset = new ConversationToolset([tool], log.For<ConversationToolset>());
        using var metrics = new MetricsCapture();

        var result = await toolset.InvokeAsync(
            new FunctionCallContent("call_1", name, new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Equal("SQL error [42601]: syntax error", result.Result?.ToString());
        Assert.Equal("error", Assert.Single(metrics.Of("wojtus.conversation.tool.calls", "tool", name)).Tags["outcome"]);
        // The tool logged its own failure: the toolset adds no Warning.
        Assert.DoesNotContain(log.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    // A REST call's own timeout throws TaskCanceledException while the turn is fine (#455): that
    // is a tool failure for the model, not the end of the turn.
    [Fact]
    public async Task InvokeAsync_ToolThrowsOce_WithTheTurnTokenLive_ReturnsErrorString()
    {
        var tool = AIFunctionFactory.Create(
            (Func<string>)(() => throw new TaskCanceledException("REST timeout")),
            new AIFunctionFactoryOptions { Name = "slow", Description = "times out" });
        var toolset = new ConversationToolset([tool], NullLogger.Instance);

        var result = await toolset.InvokeAsync(
            new FunctionCallContent("call_1", "slow", new Dictionary<string, object?>()), CancellationToken.None);

        Assert.Contains("Error running tool", result.Result?.ToString());
    }

    [Fact]
    public async Task InvokeAsync_TurnCancelled_Unwinds()
    {
        using var cts = new CancellationTokenSource();
        var tool = AIFunctionFactory.Create(
            (Func<string>)(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }),
            new AIFunctionFactoryOptions { Name = "cancelled", Description = "the turn ends" });
        var toolset = new ConversationToolset([tool], NullLogger.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => toolset.InvokeAsync(
            new FunctionCallContent("call_1", "cancelled", new Dictionary<string, object?>()), cts.Token));
    }

    // Conversation:EnableSensitiveData gates the tool span's payload like the prompts on the MEAI
    // span (#458): off, the span keeps the name and the result size but no arguments.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ToolSpan_CarriesArgumentsOnlyWithSensitiveData(bool captureSensitiveData)
    {
        var name = $"echo_{Guid.NewGuid():N}";
        var tool = AIFunctionFactory.Create(
            (string text) => $"echo: {text}",
            new AIFunctionFactoryOptions { Name = name, Description = "echoes" });
        var toolset = new ConversationToolset([tool], NullLogger.Instance, captureSensitiveData);
        List<Activity> spans = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConversationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (spans)
                    spans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        await toolset.InvokeAsync(
            new FunctionCallContent("call_1", name, new Dictionary<string, object?> { ["text"] = "sekret" }),
            CancellationToken.None);

        Activity span;
        lock (spans)
            span = Assert.Single(spans, s => s.DisplayName == $"tool {name}");
        Assert.Equal(name, span.GetTagItem("tool.name"));
        Assert.NotNull(span.GetTagItem("tool.result_length"));
        Assert.Equal(captureSensitiveData ? "text=sekret" : null, span.GetTagItem("tool.arguments"));
    }

    [Fact]
    public async Task InvokeAsync_UnknownTool_ReturnsErrorString()
    {
        var toolset = new ConversationToolset([], NullLogger.Instance);

        var result = await toolset.InvokeAsync(
            new FunctionCallContent("call_2", "does_not_exist", new Dictionary<string, object?>()),
            CancellationToken.None);

        Assert.Equal("call_2", result.CallId);
        Assert.Contains("unknown tool", result.Result?.ToString());
    }
}
