using System.Diagnostics;

namespace DiscordEventService.Services.Conversation;

internal static class ConversationTelemetry
{
    // Shared by the MEAI UseOpenTelemetry(sourceName:) pipeline and the root
    // TracerProvider's AddSource(). The two MUST match exactly or no spans export.
    public const string SourceName = "DiscordEventService.Conversation";

    // The same registered source carries our hand-rolled spans (the per-turn parent
    // and each tool dispatch), so they nest under the MEAI generation spans in
    // Langfuse without a second AddSource(). No listener (Langfuse unconfigured) =>
    // StartActivity returns null, so every caller must null-check.
    public static readonly ActivitySource ActivitySource = new(SourceName);
}
