namespace DiscordEventService.Services.Conversation;

// The out-of-band invocation context for a conversation turn: who triggered it and
// where. Captured by the handler from the Discord event and threaded through the
// service into the tools — deliberately NOT model-controlled, so an injected prompt
// can never spoof the caller's identity or scope (the §6 admin gate builds on this).
//
// GuildId is null in a DM; the guild-scoped tools of ConversationToolRegistry then fall back
// to ConversationOptions.PrimaryGuildId. query_database never reads it for scope: the guild
// id only goes into its description.
//
// IsAdmin and ChannelId are also out-of-band (§6): IsAdmin is the un-promptable admin gate
// for action tools (decided by the handler from ConversationOptions.AdminUserIds, NEVER a
// model parameter), and ChannelId is the reply surface a staged action posts its confirm
// button into.
internal sealed record ConversationContext(
    ulong? GuildId,
    ulong InvokerId,
    string InvokerDisplayName,
    bool IsAdmin,
    ulong ChannelId);
