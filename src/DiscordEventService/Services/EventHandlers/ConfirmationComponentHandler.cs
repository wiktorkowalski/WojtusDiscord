using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.EventHandlers;

// Handles clicks on the §6 confirm/cancel buttons — the codebase's first interactive component.
// The button is what catches the *model* mis-parsing intent, and the load-bearing re-checks live
// here: the CLICKER (not the original requester) must be an admin, re-evaluated against
// AdminUserIds at click time, and the staged action is claimed (removed) before it runs, so a
// double-click can never fire it twice.
internal sealed class ConfirmationComponentHandler(
    IConfirmationService confirmations,
    IOptions<ConversationOptions> options,
    ILogger<ConfirmationComponentHandler> logger) : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
    {
        // Every component interaction in the guild fires this — only act on our own buttons.
        if (!ConfirmationService.TryParseCustomId(e.Id, out var kind, out var token))
            return;

        try
        {
            // Re-check the CLICKER (never the requester) against the admin allow-list, every click.
            // A non-admin's click does not consume the token, so an admin can still confirm later.
            if (!options.Value.IsAdmin(e.User.Id))
            {
                await RespondEphemeralAsync(e, "Only a server admin can confirm or cancel this action.");
                return;
            }

            if (kind == ConfirmKind.Cancel)
            {
                await CancelAsync(e, token);
                return;
            }

            await ConfirmAsync(e, token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Confirmation click failed for token {Token}", token);
        }
    }

    private async Task ConfirmAsync(ComponentInteractionCreatedEventArgs e, string token)
    {
        // Acknowledge the click FIRST (the write can exceed the 3s window), THEN claim. Claiming
        // before the ack would silently lose a confirmed irreversible action if the ack threw — the
        // token would already be gone and a re-click couldn't retry. With ack-first, a failed ack
        // leaves the action staged and retryable.
        await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

        // Claim BEFORE executing: the first click removes the action, so a double-click finds nothing.
        // The loser already acked, so it answers with a follow-up (CreateResponse is spent).
        if (!confirmations.TryClaim(token, out var action))
        {
            await e.Interaction.CreateFollowupMessageAsync(new DiscordFollowupMessageBuilder()
                .AsEphemeral()
                .WithContent("That action was already handled or has expired."));
            return;
        }

        string outcome;
        try
        {
            outcome = await action.ExecuteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Staged action {Token} threw while executing", token);
            outcome = "The action failed to run.";
        }

        logger.LogInformation("Action {Token} confirmed by {ClickerId}: {Outcome}", token, e.User.Id, outcome);

        // The action already ran; a failed feedback edit must not hide that — fall back to a channel
        // message so the admin always sees the outcome.
        var feedback = $"✅ {outcome}\n-# confirmed by {e.User.Username}";
        try
        {
            await e.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(feedback));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action {Token} ran but editing the confirm prompt failed; sending a fallback", token);
            try
            {
                await e.Channel.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent(feedback)
                    .WithAllowedMentions(Mentions.None));
            }
            catch (Exception fallbackEx)
            {
                logger.LogWarning(fallbackEx, "Fallback outcome message for {Token} also failed", token);
            }
        }
    }

    private async Task CancelAsync(ComponentInteractionCreatedEventArgs e, string token)
    {
        if (!confirmations.TryClaim(token, out var action))
        {
            await RespondEphemeralAsync(e, "That action was already handled or has expired.");
            return;
        }

        logger.LogInformation("Action {Token} cancelled by {ClickerId}", token, e.User.Id);
        await e.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder()
                .WithContent($"❌ Cancelled: {action.Description}\n-# cancelled by {e.User.Username}"));
    }

    private static Task RespondEphemeralAsync(ComponentInteractionCreatedEventArgs e, string content) =>
        e.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral().WithContent(content));
}
