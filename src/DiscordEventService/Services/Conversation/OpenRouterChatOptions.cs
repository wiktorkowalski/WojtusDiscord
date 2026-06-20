using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace DiscordEventService.Services.Conversation;

// Builds the per-request ChatOptions for the OpenRouter chat model, injecting the
// Claude/OpenRouter body fields (`provider`, `reasoning`) through a JsonPatch on a
// fresh ChatCompletionOptions. ChatOptions.AdditionalProperties is NOT the mechanism —
// the OpenAI adapter silently drops that dictionary (except the reserved `strict` key).
// Reused unchanged by every round once the agentic loop lands (§2+).
internal static class OpenRouterChatOptions
{
    public static ChatOptions Create(string reasoningEffort) => new()
    {
        RawRepresentationFactory = _ => BuildRawRepresentation(reasoningEffort),
    };

    private static ChatCompletionOptions BuildRawRepresentation(string reasoningEffort)
    {
#pragma warning disable SCME0001 // ChatCompletionOptions.Patch (JsonPatch) is experimental.
        var options = new ChatCompletionOptions();

        // Pin the provider to Anthropic and forbid a silent fallback to a different host.
        options.Patch.Set("$.provider"u8,
            BinaryData.FromObjectAsJson(new { order = new[] { "anthropic" }, allow_fallbacks = false }));

        // Request reasoning tokens at the configured effort.
        options.Patch.Set("$.reasoning"u8,
            BinaryData.FromObjectAsJson(new { effort = reasoningEffort }));

        return options;
#pragma warning restore SCME0001
    }
}
