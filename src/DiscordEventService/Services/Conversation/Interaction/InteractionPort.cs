namespace DiscordEventService.Services.Conversation.Interaction;

// The Discord-free interaction port (#308). Everything the conversation and confirmation
// flows need from Discord is expressed here as plain ids, strings and narrow async
// surfaces, so the load-bearing policy behind them — the clicker admin re-check, the
// ack/claim ordering, the routing rules — is reachable from a plain unit test with
// hand-written fakes. The DSharpPlus implementations live next to the event handlers
// (DiscordInteractionAdapters.cs) and hold translation only, never a decision.

// One inbound Discord message, flattened to the fields the routing rules actually read.
// CachedThreadCreatorId is what the gateway's cached channel already knew (null when the
// cache lacked the metadata — the flow then asks the gateway to resolve it).
internal sealed record IncomingConversationMessage(
    ulong MessageId,
    ulong ChannelId,
    ulong? GuildId,
    ulong AuthorId,
    bool AuthorIsBot,
    string AuthorDisplayName,
    string Content,
    bool ChannelIsThread,
    ulong? CachedThreadCreatorId,
    IReadOnlyList<ulong> MentionedUserIds);

// Where a turn's messages go. Deliberately token-free: posting the buffered answer has to
// work after the turn's timeout token is already cancelled.
internal interface ITurnSurface
{
    // The Discord channel id — the reply surface a staged action posts its confirm button into.
    ulong ChannelId { get; }

    Task SendAsync(string content);

    Task TriggerTypingAsync();
}

// The per-event Discord reach the conversation flow needs beyond posting: resolving a
// thread's creator when the cached channel didn't carry it, and spawning the thread a
// fresh @mention converses in.
internal interface IConversationGateway
{
    // The channel the triggering message arrived in.
    ITurnSurface Origin { get; }

    // Null when the creator can't be determined.
    Task<ulong?> ResolveThreadCreatorAsync();

    Task<ITurnSurface> CreateThreadAsync(string name);
}

// A click on one of the §6 confirm/cancel buttons, flattened. The clicker — never the
// requester — is who the flow re-checks against the admin allow-list. GuildId scopes the
// feedback turn's tools (#310); null only if the card somehow lives in a DM.
internal sealed record ConfirmationClick(string CustomId, ulong ClickerId, string ClickerName, ulong? GuildId);

// What a §6 click resolved to, handed from ConfirmationFlow to ConversationFlow (#310) so
// the model learns what happened instead of the conversation dead-ending at the card.
// Result is what the action's execute returned; null means Cancel — it never ran.
internal sealed record StagedActionOutcome(
    string Description, string? Result, ulong ClickerId, string ClickerName, ulong? GuildId);

// The four ways a component interaction can answer, plus the channel fallback. An
// interaction has exactly ONE response slot: RespondEphemeralAsync, AcknowledgeAsync and
// ReplacePromptAsync each spend it, and only a follow-up works afterwards — the ordering
// rules in ConfirmationFlow depend on that.
internal interface IConfirmationSurface
{
    // Spends the response slot on a private message to the clicker.
    Task RespondEphemeralAsync(string content);

    // Deferred message update: spends the response slot to stop the 3s clock without
    // changing anything visible, so a slow action can still run.
    Task AcknowledgeAsync();

    // Only valid once the response slot is spent.
    Task FollowupEphemeralAsync(string content);

    // Edits the confirm prompt after AcknowledgeAsync deferred it.
    Task EditPromptAsync(string content);

    // Spends the response slot by replacing the confirm prompt in place.
    Task ReplacePromptAsync(string content);

    // Last resort when editing the prompt failed but the action already ran.
    Task SendChannelFallbackAsync(string content);
}
