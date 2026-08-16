using DiscordEventService.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation.Interaction;

// What a click on the §6 confirm/cancel buttons does, Discord-free (#308). The button is
// what catches the *model* mis-parsing intent, so the load-bearing re-checks live here:
// the CLICKER (not the original requester) must be an admin, re-evaluated against
// AdminUserIds at click time, and the staged action is claimed (removed) before it runs,
// so a double-click can never fire it twice.
internal sealed class ConfirmationFlow(
    IConfirmationService confirmations,
    ConversationFlow conversation,
    IOptions<ConversationOptions> options,
    ILogger<ConfirmationFlow> logger)
{
    // feedbackSurface is the channel the card lives in — where the outcome feedback turn
    // (#310) posts, and the same channel the staging turn conversed in.
    public async Task HandleAsync(ConfirmationClick click, IConfirmationSurface surface, ITurnSurface feedbackSurface)
    {
        // Every component interaction in the guild reaches this — only act on our own buttons.
        if (!ConfirmationService.TryParseCustomId(click.CustomId, out var kind, out var token))
            return;

        try
        {
            // Re-check the CLICKER (never the requester) against the admin allow-list, every click.
            // A non-admin's click does not consume the token, so an admin can still confirm later.
            if (!options.Value.IsAdmin(click.ClickerId))
            {
                await surface.RespondEphemeralAsync("Only a server admin can confirm or cancel this action.");
                return;
            }

            if (kind == ConfirmKind.Cancel)
            {
                await CancelAsync(click, surface, feedbackSurface, token);
                return;
            }

            await ConfirmAsync(click, surface, feedbackSurface, token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Confirmation click failed for token {Token}", token);
        }
    }

    private async Task ConfirmAsync(
        ConfirmationClick click, IConfirmationSurface surface, ITurnSurface feedbackSurface, string token)
    {
        // Acknowledge the click FIRST (the write can exceed the 3s window), THEN claim. Claiming
        // before the ack would silently lose a confirmed irreversible action if the ack threw — the
        // token would already be gone and a re-click couldn't retry. With ack-first, a failed ack
        // leaves the action staged and retryable.
        await surface.AcknowledgeAsync();

        // Claim BEFORE executing: the first click removes the action, so a double-click finds nothing.
        // The loser already acked, so it answers with a follow-up (the response slot is spent).
        if (!confirmations.TryClaim(token, out var action))
        {
            await surface.FollowupEphemeralAsync("That action was already handled or has expired.");
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

        logger.LogInformation("Action {Token} confirmed by {ClickerId}: {Outcome}", token, click.ClickerId, outcome);

        // The action already ran; a failed feedback edit must not hide that — fall back to a channel
        // message so the admin always sees the outcome.
        var feedback = $"✅ {outcome}\n-# confirmed by {click.ClickerName}";
        try
        {
            await surface.EditPromptAsync(feedback);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action {Token} ran but editing the confirm prompt failed; sending a fallback", token);
            try
            {
                await surface.SendChannelFallbackAsync(feedback);
            }
            catch (Exception fallbackEx)
            {
                logger.LogWarning(fallbackEx, "Fallback outcome message for {Token} also failed", token);
            }
        }

        await FeedOutcomeBackAsync(
            new StagedActionOutcome(action.Description, outcome, click.ClickerId, click.ClickerName, click.GuildId),
            feedbackSurface, token);
    }

    private async Task CancelAsync(
        ConfirmationClick click, IConfirmationSurface surface, ITurnSurface feedbackSurface, string token)
    {
        if (!confirmations.TryClaim(token, out var action))
        {
            await surface.RespondEphemeralAsync("That action was already handled or has expired.");
            return;
        }

        logger.LogInformation("Action {Token} cancelled by {ClickerId}", token, click.ClickerId);
        await surface.ReplacePromptAsync(
            $"❌ Cancelled: {action.Description}\n-# cancelled by {click.ClickerName}");

        await FeedOutcomeBackAsync(
            new StagedActionOutcome(action.Description, Result: null, click.ClickerId, click.ClickerName, click.GuildId),
            feedbackSurface, token);
    }

    // #310: the click carries the thread forward — hand the outcome to the conversation as
    // its own turn. Isolated failure handling: the card is already edited by now, so a
    // broken feedback turn must not read as "the action failed".
    private async Task FeedOutcomeBackAsync(StagedActionOutcome outcome, ITurnSurface surface, string token)
    {
        try
        {
            await conversation.HandleActionOutcomeAsync(outcome, surface);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outcome feedback turn failed for action {Token}", token);
        }
    }
}
