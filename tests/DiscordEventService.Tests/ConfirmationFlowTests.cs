using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The §6 confirm-button contract, reachable without Discord now that the policy sits behind
// the interaction port (#308). These are the re-checks the button exists for: the button is
// what catches the *model* mis-parsing intent, so the CLICKER is re-authorized at click time
// and the staged action is claimed before it runs.
public sealed class ConfirmationFlowTests
{
    private const ulong AdminId = 11UL;
    private const ulong OutsiderId = 22UL;
    private const ulong GuildId = 900UL;
    private const ulong CardChannelId = 777UL;
    private const string Token = "abc123";
    private const string ConfirmId = $"conv6:confirm:{Token}";
    private const string CancelId = $"conv6:cancel:{Token}";

    private readonly List<string> _trace = [];
    private readonly RecordingLogger _logger = new();
    private readonly FakeStagedActionStore _confirmations;
    private readonly FakeConfirmationSurface _surface;
    private readonly FakeTurnSource _feedbackTurns = new();
    private readonly FakeTurnSurface _feedbackSurface = new(CardChannelId);
    private readonly FakeUsageAlertService _feedbackUsageAlerts = new();
    private readonly ConfirmationFlow _flow;

    private int _executions;

    public ConfirmationFlowTests()
    {
        _confirmations = new FakeStagedActionStore(_trace);
        _surface = new FakeConfirmationSurface(_trace);
        var options = Options.Create(new ConversationOptions { AdminUserIds = [AdminId] });
        // The real ConversationFlow over fakes — the #310 feedback path is covered
        // click-to-rendered-reply, not against a stub of the flow.
        _flow = new ConfirmationFlow(
            _confirmations,
            new ConversationFlow(
                _feedbackTurns, _feedbackUsageAlerts, options, _logger.For<ConversationFlow>()),
            options,
            _logger.For<ConfirmationFlow>());
    }

    [Theory]
    [InlineData("someone-elses-button")]
    [InlineData("")]
    public async Task ForeignCustomId_IsIgnoredEntirely(string customId)
    {
        Stage();

        await _flow.HandleAsync(Click(customId, AdminId), _surface, _feedbackSurface);

        // Not even a claim attempt — every component interaction in the guild reaches here.
        Assert.Empty(_trace);
        Assert.True(_confirmations.Contains(Token));
    }

    [Fact]
    public async Task NonAdminClicker_IsRefused_AndTheActionStaysClaimableByAnAdmin()
    {
        Stage();

        await _flow.HandleAsync(Click(ConfirmId, OutsiderId), _surface, _feedbackSurface);

        Assert.Equal(["ephemeral:Only a server admin can confirm or cancel this action."], _trace);
        Assert.Equal(0, _executions);
        // The refused click must NOT consume the token.
        Assert.True(_confirmations.Contains(Token));

        _trace.Clear();
        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);
        Assert.Equal(1, _executions);
    }

    [Fact]
    public async Task Confirm_AcknowledgesBeforeClaiming_AndClaimsBeforeExecuting()
    {
        Stage();

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(["ack", "claim", "execute", "edit:✅ done\n-# confirmed by admin"], _trace);
    }

    [Fact]
    public async Task DoubleClick_RunsTheActionOnce_AndTellsTheLoserViaFollowup()
    {
        Stage();

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);
        _trace.Clear();
        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(1, _executions);
        // The loser already spent its response slot on the ack, so it can only follow up.
        Assert.Equal(["ack", "claim", "followup:That action was already handled or has expired."], _trace);
    }

    [Fact]
    public async Task FailedAcknowledge_LeavesTheActionStagedAndUnexecuted()
    {
        Stage();
        _surface.AcknowledgeFailure = new InvalidOperationException("interaction expired");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        // Claiming before the ack would have lost a confirmed irreversible action for good.
        Assert.Equal(0, _executions);
        Assert.True(_confirmations.Contains(Token));
        Assert.Contains(_logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains(Token));
    }

    [Fact]
    public async Task ActionThatThrows_IsReportedAsFailure_NotSilence()
    {
        Stage(_ => throw new InvalidOperationException("Discord said no"));

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Contains("edit:✅ The action failed to run.\n-# confirmed by admin", _trace);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task FailedPromptEdit_FallsBackToAChannelMessage_SoTheOutcomeIsNeverHidden()
    {
        Stage();
        _surface.EditFailure = new InvalidOperationException("unknown message");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(1, _executions);
        Assert.Contains("fallback:✅ done\n-# confirmed by admin", _trace);
    }

    [Fact]
    public async Task FailedPromptEditAndFailedFallback_AreSwallowed_TheActionAlreadyRan()
    {
        Stage();
        _surface.EditFailure = new InvalidOperationException("unknown message");
        _surface.FallbackFailure = new InvalidOperationException("missing permissions");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(1, _executions);
        Assert.Equal(2, _logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task Cancel_ReplacesThePrompt_ConsumesTheToken_AndNeverExecutes()
    {
        Stage();

        await _flow.HandleAsync(Click(CancelId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(["claim", "replace:❌ Cancelled: ban someone\n-# cancelled by admin"], _trace);
        Assert.Equal(0, _executions);
        Assert.False(_confirmations.Contains(Token));
    }

    [Fact]
    public async Task Cancel_OnAnExpiredToken_SaysSo()
    {
        await _flow.HandleAsync(Click(CancelId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(["claim", "ephemeral:That action was already handled or has expired."], _trace);
    }

    private void Stage(Func<CancellationToken, Task<string>>? execute = null) =>
        _confirmations.Stage(Token, new PendingGuildAction(
            Token,
            OutsiderId,
            "ban someone",
            execute ?? (_ =>
            {
                _executions++;
                _trace.Add("execute");
                return Task.FromResult("done");
            })));

    // #310: the outcome feeds back into the conversation as its own turn.

    [Fact]
    public async Task Confirm_FeedsTheOutcomeBackIntoTheConversation_AsTheClicker()
    {
        Stage();
        _feedbackTurns.Updates.Add(new ConversationUpdate.AssistantTextDelta("zbanowane, szefie"));

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        var (message, context) = Assert.Single(_feedbackTurns.Turns);
        // The model gets the description AND what the execute actually returned.
        Assert.Contains("ban someone", message);
        Assert.Contains("done", message);
        Assert.Contains("confirmed", message);
        // The invocation context is the CLICKER on the card's channel — same conversation
        // memory window as the turn that staged the action.
        Assert.Equal(AdminId, context.InvokerId);
        Assert.True(context.IsAdmin);
        Assert.Equal(GuildId, context.GuildId);
        Assert.Equal(CardChannelId, context.ChannelId);
        Assert.Equal(["zbanowane, szefie"], _feedbackSurface.Sent);
        // Every click is a model turn now — the §3 cost-cap check must cover it.
        Assert.Equal([AdminId], _feedbackUsageAlerts.Checked);
    }

    [Fact]
    public async Task AFailedAction_IsFedBackToo_SoTheModelCanProposeTheFix()
    {
        Stage(_ => Task.FromResult("I don't have permission to do that."));

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Contains("I don't have permission to do that.", _feedbackTurns.Turns.Single().Message);
    }

    [Fact]
    public async Task Cancel_FeedsBack_WithTheCancelledFraming()
    {
        Stage();

        await _flow.HandleAsync(Click(CancelId, AdminId), _surface, _feedbackSurface);

        var (message, context) = Assert.Single(_feedbackTurns.Turns);
        Assert.Contains("cancelled", message);
        Assert.Contains("ban someone", message);
        Assert.Equal(AdminId, context.InvokerId);
        Assert.Equal(0, _executions);
    }

    [Fact]
    public async Task RefusedForeignAndAlreadyHandledClicks_ProduceNoFeedbackTurn()
    {
        Stage();

        await _flow.HandleAsync(Click("someone-elses-button", AdminId), _surface, _feedbackSurface);
        await _flow.HandleAsync(Click(ConfirmId, OutsiderId), _surface, _feedbackSurface);
        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);
        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);
        await _flow.HandleAsync(Click(CancelId, AdminId), _surface, _feedbackSurface);

        // Only the one winning confirm fed back — the loser and the stale cancel did not.
        Assert.Single(_feedbackTurns.Turns);
        Assert.Equal(1, _executions);
    }

    [Fact]
    public async Task AFailedFeedbackTurn_IsLogged_AndDoesNotUndoTheHandledClick()
    {
        Stage();
        _feedbackTurns.Failure = new InvalidOperationException("model exploded");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        // The action ran and the card was edited before the feedback turn broke.
        Assert.Equal(1, _executions);
        Assert.Contains("edit:✅ done\n-# confirmed by admin", _trace);
        Assert.Contains(_logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("feedback"));
    }

    [Fact]
    public async Task AnUnconfiguredConversation_SkipsTheFeedbackTurn_ButStillHandlesTheClick()
    {
        Stage();
        _feedbackTurns.IsConfigured = false;

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface, _feedbackSurface);

        Assert.Equal(1, _executions);
        Assert.Empty(_feedbackTurns.Turns);
        Assert.Empty(_feedbackSurface.Sent);
    }

    private static ConfirmationClick Click(string customId, ulong clickerId) =>
        new(customId, clickerId, clickerId == AdminId ? "admin" : "outsider", GuildId);
}
