using System.ClientModel.Primitives;
using System.Text.Json;
using DiscordEventService.Services.Conversation;
using OpenAI.Chat;
using Xunit;

namespace DiscordEventService.Tests;

// Guards the least-visible, highest-risk part of the conversational wire: the Claude/
// OpenRouter body fields must land via JsonPatch on the serialized request — they are
// silently dropped if routed through ChatOptions.AdditionalProperties instead.
public sealed class OpenRouterChatOptionsTests
{
    private static JsonElement SerializeRequestBody(string reasoningEffort)
    {
        var chatOptions = OpenRouterChatOptions.Create(reasoningEffort);
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
}
