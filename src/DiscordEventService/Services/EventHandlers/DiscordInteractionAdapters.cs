using DiscordEventService.Services.Conversation.Interaction;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

// The DSharpPlus half of the interaction port (#308): translation only, no branching
// policy — every decision lives in ConversationFlow / ConfirmationFlow, which never see a
// DSharpPlus type. Anything added here that needs a test belongs on the other side.

// Posts a turn's messages into one channel (or thread). Mentions are always suppressed:
// model text may contain @mentions.
internal sealed class DiscordTurnSurface(DiscordChannel channel) : ITurnSurface
{
    public ulong ChannelId => channel.Id;

    public Task SendAsync(string content) =>
        channel.SendMessageAsync(new DiscordMessageBuilder().WithContent(content).WithAllowedMentions(Mentions.None));

    public Task TriggerTypingAsync() => channel.TriggerTypingAsync();
}

// The per-event Discord reach for one incoming message: the channel it arrived in, the
// thread-creator lookup for a cache miss, and thread creation off the triggering message.
internal sealed class DiscordConversationGateway(DiscordClient client, MessageCreatedEventArgs e)
    : IConversationGateway
{
    public ITurnSurface Origin { get; } = new DiscordTurnSurface(e.Channel);

    public async Task<ulong?> ResolveThreadCreatorAsync()
    {
        var fetched = await client.GetChannelAsync(e.Channel.Id);
        return (fetched as DiscordThreadChannel)?.CreatorId;
    }

    public async Task<ITurnSurface> CreateThreadAsync(string name) =>
        new DiscordTurnSurface(await e.Message.CreateThreadAsync(name, DiscordAutoArchiveDuration.Day));
}

// The five response shapes a component interaction can take. An interaction has exactly
// one response slot — which of these spends it is the contract ConfirmationFlow relies on.
internal sealed class DiscordConfirmationSurface(ComponentInteractionCreatedEventArgs e) : IConfirmationSurface
{
    public Task RespondEphemeralAsync(string content) =>
        e.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral().WithContent(content));

    public Task AcknowledgeAsync() =>
        e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

    public Task FollowupEphemeralAsync(string content) =>
        e.Interaction.CreateFollowupMessageAsync(
            new DiscordFollowupMessageBuilder().AsEphemeral().WithContent(content));

    public Task EditPromptAsync(string content) =>
        e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(content));

    public Task ReplacePromptAsync(string content) =>
        e.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder().WithContent(content));

    public Task SendChannelFallbackAsync(string content) =>
        e.Channel.SendMessageAsync(new DiscordMessageBuilder()
            .WithContent(content)
            .WithAllowedMentions(Mentions.None));
}
