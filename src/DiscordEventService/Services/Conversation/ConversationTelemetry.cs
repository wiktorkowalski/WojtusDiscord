namespace DiscordEventService.Services.Conversation;

internal static class ConversationTelemetry
{
    // Shared by the MEAI UseOpenTelemetry(sourceName:) pipeline and the root
    // TracerProvider's AddSource(). The two MUST match exactly or no spans export.
    public const string SourceName = "DiscordEventService.Conversation";
}
