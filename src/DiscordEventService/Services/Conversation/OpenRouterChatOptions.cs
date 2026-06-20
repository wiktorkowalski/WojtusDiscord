using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace DiscordEventService.Services.Conversation;

// Builds the per-request ChatOptions for the OpenRouter chat model, injecting the
// Claude/OpenRouter body fields (`provider`, `reasoning`, `usage`) through a JsonPatch on
// a fresh ChatCompletionOptions. ChatOptions.AdditionalProperties is NOT the mechanism —
// the OpenAI adapter silently drops that dictionary (except the reserved `strict` key).
// Reused unchanged by every round of the agentic loop (§2+).
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

        // Ask OpenRouter to include the real upstream `usage.cost` (USD) on the final
        // chunk — it is not in MEAI's typed UsageDetails and is recovered from the raw
        // ChatTokenUsage patch (#241). Captured per round for the §5 usage ledger.
        options.Patch.Set("$.usage"u8, BinaryData.FromObjectAsJson(new { include = true }));

        return options;
#pragma warning restore SCME0001
    }
}
