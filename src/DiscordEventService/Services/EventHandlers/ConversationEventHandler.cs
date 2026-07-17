using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.EventHandlers;

// The conversational entrypoint (#238 §1): an @mention in an allow-listed channel
// spawns a thread, a DM replies inline, and follow-ups inside a bot-owned thread
// continue without a re-mention. Deliberately BYPASSES EventPipeline — a conversation
// turn is not an ingested event (no raw_event row) and a DM has no guild id.
internal sealed class ConversationEventHandler(
    ConversationService conversation,
    IOptions<ConversationOptions> options,
    ILogger<ConversationEventHandler> logger) : IEventHandler<MessageCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, MessageCreatedEventArgs e)
    {
        var author = e.Author;
        var botId = sender.CurrentUser.Id;

        // Skip our own and other bots' messages — otherwise the bot's own in-thread reply
        // re-triggers this handler and loops forever.
        if (author is null || author.Id == botId || author.IsBot)
            return;

        // Stay inert (no thread, no reply) when the bot has no usable OpenRouter key.
        if (!conversation.IsConfigured)
            return;

        try
        {
            // Inversion vs the ingestion handlers: a null guild is a DM, which is allowed.
            if (e.Guild is null)
            {
                await RespondAsync(e.Channel, e);
                return;
            }

            // A follow-up inside a thread the bot started — no re-mention required.
            if (await IsBotOwnedThreadAsync(sender, e.Channel, botId))
            {
                await RespondAsync(e.Channel, e);
                return;
            }

            // A fresh @mention in an allow-listed channel — spawn a thread, converse there.
            if (IsBotMentioned(e.Message, botId) && options.Value.ChannelAllowList.Contains(e.Channel.Id))
            {
                var thread = await e.Message.CreateThreadAsync(
                    BuildThreadName(e.Message, botId), DiscordAutoArchiveDuration.Day);
                logger.LogDebug("Started conversation thread {ThreadId} from message {MessageId}",
                    thread.Id, e.Message.Id);
                await RespondAsync(thread, e);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversation handler failed for message {MessageId} in channel {ChannelId}",
                e.Message.Id, e.Channel.Id);
        }
    }

    private async Task RespondAsync(DiscordChannel target, MessageCreatedEventArgs e)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        // The out-of-band invocation context: a null guild is a DM. Captured from the
        // event, never from the model, so a tool's scope can't be spoofed by a prompt.
        // IsAdmin (the §6 action gate) is decided here from the allow-list — never a model
        // parameter — and ChannelId is the surface a staged action posts its confirm button into.
        var context = new ConversationContext(
            e.Guild?.Id,
            e.Author.Id,
            e.Author.GlobalName ?? e.Author.Username,
            options.Value.IsAdmin(e.Author.Id),
            target.Id);

        // Render the agentic loop's events into Discord as discrete messages (#274): deltas
        // are buffered, each tool round posts one standalone cue+summary message, and the
        // final answer is posted complete when the round finishes — no edit-in-place.
        var renderer = new DiscordTurnRenderer(target);
        try
        {
            // Show the bot working before the first model token arrives.
            await renderer.TriggerTypingAsync();

            await foreach (var update in conversation.GenerateReplyAsync(e.Message.Content, context, cts.Token))
            {
                switch (update)
                {
                    case ConversationUpdate.AssistantTextDelta delta:
                        await renderer.AppendDeltaAsync(delta.Text);
                        break;

                    case ConversationUpdate.ToolBatchSummary summary:
                        await renderer.CompleteRoundAsync(summary.Text);
                        break;
                }
            }

            // The deltas after the last tool round are the answer.
            await renderer.CompleteTurnAsync();

            logger.LogInformation("Conversation reply for {Author} sent {MessageCount} message(s)",
                context.InvokerDisplayName, renderer.MessageCount);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogWarning("Conversation turn timed out after {Timeout}s for message {MessageId}",
                options.Value.RequestTimeoutSeconds, e.Message.Id);

            // Best-effort: post whatever the model had streamed so the user isn't left with
            // silence after the wait (the buffer holds it; posting doesn't need the token).
            await renderer.CompleteTurnAsync();
        }
    }

    // Recognize a thread the bot started via its creator == the bot (live lookup; there
    // is no conversation store until §5).
    private async Task<bool> IsBotOwnedThreadAsync(DiscordClient sender, DiscordChannel channel, ulong botId)
    {
        if (channel.Type is not (DiscordChannelType.PublicThread
            or DiscordChannelType.PrivateThread or DiscordChannelType.NewsThread))
            return false;

        var creatorId = (channel as DiscordThreadChannel)?.CreatorId ?? 0;
        if (creatorId == 0)
        {
            // Cached channel lacked creator metadata — resolve it from the API.
            try
            {
                var fetched = await sender.GetChannelAsync(channel.Id);
                creatorId = (fetched as DiscordThreadChannel)?.CreatorId ?? 0;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not resolve creator of thread {ChannelId}", channel.Id);
            }
        }

        return creatorId == botId;
    }

    private static bool IsBotMentioned(DiscordMessage message, ulong botId) =>
        message.MentionedUsers.Any(user => user.Id == botId);

    // Discord caps a thread name at 100 characters; trim with margin.
    private const int MaxThreadNameLength = 90;

    private static string BuildThreadName(DiscordMessage message, ulong botId)
    {
        var text = (message.Content ?? string.Empty)
            .Replace($"<@{botId}>", string.Empty)
            .Replace($"<@!{botId}>", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(text))
            return "Chat with Wojtuś";

        return text.Length <= MaxThreadNameLength ? text : text[..MaxThreadNameLength];
    }
}
