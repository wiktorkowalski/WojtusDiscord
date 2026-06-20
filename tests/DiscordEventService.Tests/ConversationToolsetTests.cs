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
