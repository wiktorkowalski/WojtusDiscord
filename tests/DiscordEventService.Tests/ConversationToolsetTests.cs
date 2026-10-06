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
