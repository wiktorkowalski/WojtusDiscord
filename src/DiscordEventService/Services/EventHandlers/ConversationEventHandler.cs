using DiscordEventService.Services.Conversation.Interaction;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

// The DSharpPlus adapter for the conversational entrypoint (#238 §1): flatten the gateway
// event into the Discord-free port and hand it to ConversationFlow, which owns every
// routing decision (#308). Nothing here may branch on policy.
internal sealed class ConversationEventHandler(ConversationFlow flow) : IEventHandler<MessageCreatedEventArgs>
{
    public Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
    {
        // No author means no invoker identity to gate on — nothing the flow could route.
        if (e.Author is null)
            return Task.CompletedTask;

        var message = new IncomingConversationMessage(
            e.Message.Id,
            e.Channel.Id,
            e.Guild?.Id,
            e.Author.Id,
            e.Author.IsBot,
            e.Author.GlobalName ?? e.Author.Username,
            e.Message.Content ?? string.Empty,
            IsThread(e.Channel),
            // 0 means the cached channel carried no creator metadata — the flow then asks
            // the gateway to resolve it from the API.
            (e.Channel as DiscordThreadChannel)?.CreatorId,
            [.. e.Message.MentionedUsers.Select(user => user.Id)]);

        return flow.HandleAsync(message, sender.CurrentUser.Id, new DiscordConversationGateway(sender, e));
    }

    private static bool IsThread(DiscordChannel channel) =>
        channel.Type is DiscordChannelType.PublicThread
            or DiscordChannelType.PrivateThread
            or DiscordChannelType.NewsThread;
}
