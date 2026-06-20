using DiscordEventService.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// Owns the model round-trip for a conversation turn. §1 is a single round-trip — no
// tools, no loop, no memory. §2+ grow the manual agentic loop in here; the Discord
// plumbing (threads, DMs, chunking) stays in ConversationEventHandler.
internal sealed class ConversationService(
    IChatClient chatClient,
    IOptions<ConversationOptions> conversationOptions,
    IOptions<OpenRouterOptions> openRouterOptions,
    ILogger<ConversationService> logger)
{
    // Whether the chat client has a usable OpenRouter key — the handler checks this before
    // doing anything visible (e.g. spawning a thread) so an unconfigured bot stays inert.
    public bool IsConfigured => openRouterOptions.Value.IsConfigured;

    public async Task<string?> GenerateReplyAsync(
        string? userMessage, string authorDisplayName, CancellationToken cancellationToken)
    {
        // The chat client is built with the OpenRouter key; gate here so an unconfigured
        // bot stays silent instead of firing a doomed 401 on every mention.
        if (!openRouterOptions.Value.IsConfigured)
        {
            logger.LogWarning("Conversation triggered but OpenRouter:ApiKey is not configured — no reply sent");
            return null;
        }

        var options = conversationOptions.Value;
        List<ChatMessage> messages =
        [
            new(ChatRole.System, BuildSystemPrompt(options)),
            new(ChatRole.User, userMessage ?? string.Empty),
        ];

        var response = await chatClient.GetResponseAsync(
            messages, OpenRouterChatOptions.Create(options.ReasoningEffort), cancellationToken);

        var reply = response.Text;
        logger.LogDebug("Conversation reply generated ({Length} chars) for {Author}",
            reply?.Length ?? 0, authorDisplayName);
        return reply;
    }

    private static string BuildSystemPrompt(ConversationOptions options) =>
        !string.IsNullOrWhiteSpace(options.SystemPrompt)
            ? options.SystemPrompt
            : """
              You are Wojtuś, a friendly Discord assistant for this server.
              Keep replies concise and conversational, and answer in the language the user writes in.
              You have no tools yet, so answer from general knowledge — and say so when you can't be sure.
              """;
}
