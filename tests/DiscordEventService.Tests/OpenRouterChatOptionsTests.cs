using System.ClientModel.Primitives;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using OpenAI.Chat;
using Xunit;

namespace DiscordEventService.Tests;

// Guards the least-visible, highest-risk part of the conversational wire: the Claude/
// OpenRouter body fields must land via JsonPatch on the serialized request — they are
// silently dropped if routed through ChatOptions.AdditionalProperties instead.
public sealed class OpenRouterChatOptionsTests
{
    private static JsonElement SerializeRequestBody(
        string reasoningEffort, WebSearchOptions? webSearch = null)
    {
        var chatOptions = OpenRouterChatOptions.Create(reasoningEffort, webSearch ?? new WebSearchOptions());
        Assert.NotNull(chatOptions.RawRepresentationFactory);

        var raw = chatOptions.RawRepresentationFactory!(null!);
        var completionOptions = Assert.IsType<ChatCompletionOptions>(raw);

        var json = ModelReaderWriter.Write(completionOptions).ToString();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void Create_PinsAnthropicProviderWithoutFallback()
    {
        var provider = SerializeRequestBody("medium").GetProperty("provider");

        Assert.Equal("anthropic", provider.GetProperty("order")[0].GetString());
        Assert.False(provider.GetProperty("allow_fallbacks").GetBoolean());
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Create_SetsConfiguredReasoningEffort(string effort)
    {
        var reasoning = SerializeRequestBody(effort).GetProperty("reasoning");

        Assert.Equal(effort, reasoning.GetProperty("effort").GetString());
    }

    // The server tool must land even when ChatOptions.Tools is empty (the round-cap
    // fallback withholds app tools) — Append has to create the array, not require one.
    [Fact]
    public void Create_WebSearchEnabled_AppendsServerToolWithConfiguredParameters()
    {
        var body = SerializeRequestBody("medium",
            new WebSearchOptions { Enabled = true, Engine = "exa", MaxResults = 3 });

        var tool = Assert.Single(body.GetProperty("tools").EnumerateArray());
        Assert.Equal("openrouter:web_search", tool.GetProperty("type").GetString());
        Assert.Equal("exa", tool.GetProperty("parameters").GetProperty("engine").GetString());
        Assert.Equal(3, tool.GetProperty("parameters").GetProperty("max_results").GetInt32());
    }

    [Fact]
    public void Create_WebSearchDisabled_BodyCarriesNoServerTool()
    {
        var body = SerializeRequestBody("medium", new WebSearchOptions { Enabled = false });

        Assert.False(body.TryGetProperty("tools", out _));
    }
}
