using DiscordEventService.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation.Interaction;

// The conversational entrypoint's policy (#238 §1), Discord-free (#308): who gets a reply
// and where it lands — an @mention in an allow-listed channel spawns a thread, a DM
// replies inline, and follow-ups inside a bot-owned thread continue without a re-mention —
// plus how one turn is driven and rendered. Deliberately BYPASSES EventPipeline: a
// conversation turn is not an ingested event (no raw_event row) and a DM has no guild id.
internal sealed class ConversationFlow(
    IConversationTurnSource conversation,
    IUsageAlertService usageAlerts,
    IOptions<ConversationOptions> options,
    ILogger<ConversationFlow> logger)
{
    // Discord caps a thread name at 100 characters; trim with margin.
    private const int MaxThreadNameLength = 90;

    public async Task HandleAsync(IncomingConversationMessage message, ulong botId, IConversationGateway gateway)
    {
        // Skip our own and other bots' messages — otherwise the bot's own in-thread reply
        // re-triggers this flow and loops forever.
        if (message.AuthorId == botId || message.AuthorIsBot)
            return;

        // Stay inert (no thread, no reply) when the bot has no usable OpenRouter key.
        if (!conversation.IsConfigured)
            return;

        try
        {
            // Inversion vs the ingestion handlers: a null guild is a DM, which is allowed.
            if (message.GuildId is null)
            {
                await RespondAsync(gateway.Origin, message);
                return;
            }

            // A follow-up inside a thread the bot started — no re-mention required.
            if (await IsBotOwnedThreadAsync(message, botId, gateway))
            {
                await RespondAsync(gateway.Origin, message);
                return;
            }

            // A fresh @mention in an allow-listed channel — spawn a thread, converse there.
            if (message.MentionedUserIds.Contains(botId)
                && options.Value.ChannelAllowList.Contains(message.ChannelId))
            {
                var thread = await gateway.CreateThreadAsync(BuildThreadName(message.Content, botId));
                logger.LogDebug("Started conversation thread {ThreadId} from message {MessageId}",
                    thread.ChannelId, message.MessageId);
                await RespondAsync(thread, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversation handler failed for message {MessageId} in channel {ChannelId}",
                message.MessageId, message.ChannelId);
        }
    }

    private async Task RespondAsync(ITurnSurface target, IncomingConversationMessage message)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));

        // The out-of-band invocation context: a null guild is a DM. Captured from the
        // event, never from the model, so a tool's scope can't be spoofed by a prompt.
        // IsAdmin (the §6 action gate) is decided here from the allow-list — never a model
        // parameter — and ChannelId is the surface a staged action posts its confirm button into.
        var context = new ConversationContext(
            message.GuildId,
            message.AuthorId,
            message.AuthorDisplayName,
            options.Value.IsAdmin(message.AuthorId),
            target.ChannelId);

        // Render the agentic loop's events as discrete messages (#274): deltas are
        // buffered, each tool round posts one standalone cue+summary message, and the
        // final answer is posted complete when the round finishes — no edit-in-place.
        var renderer = new TurnRenderer(target);
        try
        {
            await RenderTurnAsync(renderer, message.Content, context, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogWarning("Conversation turn timed out after {Timeout}s for message {MessageId}",
                options.Value.RequestTimeoutSeconds, message.MessageId);

            // Best-effort: post whatever the model had streamed so the user isn't left with
            // silence after the wait (the buffer holds it; posting doesn't need the token).
            await renderer.CompleteTurnAsync();
        }
        finally
        {
            // §3 post-turn cost-cap check (#269): the ledger rows exist on every exit
            // path of GenerateReplyAsync (answer, round cap, retry exhaustion, timeout),
            // so this finally covers them all. Never throws, and runs on its own
            // timeout — the turn token may already be cancelled here.
            await usageAlerts.CheckAndAlertAsync(context.InvokerId);
        }
    }

    private async Task RenderTurnAsync(
        TurnRenderer renderer, string content, ConversationContext context, CancellationToken cancellationToken)
    {
        // Show the bot working before the first model token arrives.
        await renderer.TriggerTypingAsync();

        await foreach (var update in conversation.GenerateReplyAsync(content, context, cancellationToken))
        {
            switch (update)
            {
                case ConversationUpdate.AssistantTextDelta delta:
                    await renderer.AppendDeltaAsync(delta.Text);
                    break;

                case ConversationUpdate.ToolBatchSummary summary:
                    await renderer.CompleteRoundAsync(summary.Text);
                    break;

                case ConversationUpdate.RoundReset:
                    renderer.ResetRound();
                    break;
            }
        }

        // The deltas after the last tool round are the answer.
        await renderer.CompleteTurnAsync();

        logger.LogInformation("Conversation reply for {Author} sent {MessageCount} message(s)",
            context.InvokerDisplayName, renderer.MessageCount);
    }

    // Recognize a thread the bot started via its creator == the bot (live lookup; there
    // is no conversation store until §5).
    private async Task<bool> IsBotOwnedThreadAsync(
        IncomingConversationMessage message, ulong botId, IConversationGateway gateway)
    {
        if (!message.ChannelIsThread)
            return false;

        var creatorId = message.CachedThreadCreatorId;
        if (creatorId is null or 0)
        {
            // Cached channel lacked creator metadata — resolve it from the API.
            try
            {
                creatorId = await gateway.ResolveThreadCreatorAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not resolve creator of thread {ChannelId}", message.ChannelId);
                return false;
            }
        }

        return creatorId == botId;
    }

    internal static string BuildThreadName(string? content, ulong botId)
    {
        var text = (content ?? string.Empty)
            .Replace($"<@{botId}>", string.Empty)
            .Replace($"<@!{botId}>", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(text))
            return "Chat with Wojtuś";

        return text.Length <= MaxThreadNameLength ? text : text[..MaxThreadNameLength];
    }
}
