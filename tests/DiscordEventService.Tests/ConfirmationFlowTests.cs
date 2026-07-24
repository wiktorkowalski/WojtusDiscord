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
    private const string Token = "abc123";
    private const string ConfirmId = $"conv6:confirm:{Token}";
    private const string CancelId = $"conv6:cancel:{Token}";

    private readonly List<string> _trace = [];
    private readonly RecordingLogger _logger = new();
    private readonly FakeStagedActionStore _confirmations;
    private readonly FakeConfirmationSurface _surface;
    private readonly ConfirmationFlow _flow;

    private int _executions;

    public ConfirmationFlowTests()
    {
        _confirmations = new FakeStagedActionStore(_trace);
        _surface = new FakeConfirmationSurface(_trace);
        _flow = new ConfirmationFlow(
            _confirmations,
            Options.Create(new ConversationOptions { AdminUserIds = [AdminId] }),
            _logger.For<ConfirmationFlow>());
    }

    [Theory]
    [InlineData("someone-elses-button")]
    [InlineData("")]
    public async Task ForeignCustomId_IsIgnoredEntirely(string customId)
    {
        Stage();

        await _flow.HandleAsync(Click(customId, AdminId), _surface);

        // Not even a claim attempt — every component interaction in the guild reaches here.
        Assert.Empty(_trace);
        Assert.True(_confirmations.Contains(Token));
    }

    [Fact]
    public async Task NonAdminClicker_IsRefused_AndTheActionStaysClaimableByAnAdmin()
    {
        Stage();

        await _flow.HandleAsync(Click(ConfirmId, OutsiderId), _surface);

        Assert.Equal(["ephemeral:Only a server admin can confirm or cancel this action."], _trace);
        Assert.Equal(0, _executions);
        // The refused click must NOT consume the token.
        Assert.True(_confirmations.Contains(Token));

        _trace.Clear();
        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);
        Assert.Equal(1, _executions);
    }

    [Fact]
    public async Task Confirm_AcknowledgesBeforeClaiming_AndClaimsBeforeExecuting()
    {
        Stage();

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);

        Assert.Equal(["ack", "claim", "execute", "edit:✅ done\n-# confirmed by admin"], _trace);
    }

    [Fact]
    public async Task DoubleClick_RunsTheActionOnce_AndTellsTheLoserViaFollowup()
    {
        Stage();

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);
        _trace.Clear();
        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);

        Assert.Equal(1, _executions);
        // The loser already spent its response slot on the ack, so it can only follow up.
        Assert.Equal(["ack", "claim", "followup:That action was already handled or has expired."], _trace);
    }

    [Fact]
    public async Task FailedAcknowledge_LeavesTheActionStagedAndUnexecuted()
    {
        Stage();
        _surface.AcknowledgeFailure = new InvalidOperationException("interaction expired");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);

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

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);

        Assert.Contains("edit:✅ The action failed to run.\n-# confirmed by admin", _trace);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task FailedPromptEdit_FallsBackToAChannelMessage_SoTheOutcomeIsNeverHidden()
    {
        Stage();
        _surface.EditFailure = new InvalidOperationException("unknown message");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);

        Assert.Equal(1, _executions);
        Assert.Contains("fallback:✅ done\n-# confirmed by admin", _trace);
    }

    [Fact]
    public async Task FailedPromptEditAndFailedFallback_AreSwallowed_TheActionAlreadyRan()
    {
        Stage();
        _surface.EditFailure = new InvalidOperationException("unknown message");
        _surface.FallbackFailure = new InvalidOperationException("missing permissions");

        await _flow.HandleAsync(Click(ConfirmId, AdminId), _surface);

        Assert.Equal(1, _executions);
        Assert.Equal(2, _logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task Cancel_ReplacesThePrompt_ConsumesTheToken_AndNeverExecutes()
    {
        Stage();

        await _flow.HandleAsync(Click(CancelId, AdminId), _surface);

        Assert.Equal(["claim", "replace:❌ Cancelled: ban someone\n-# cancelled by admin"], _trace);
        Assert.Equal(0, _executions);
        Assert.False(_confirmations.Contains(Token));
    }

    [Fact]
    public async Task Cancel_OnAnExpiredToken_SaysSo()
    {
        await _flow.HandleAsync(Click(CancelId, AdminId), _surface);

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

    private static ConfirmationClick Click(string customId, ulong clickerId) =>
        new(customId, clickerId, clickerId == AdminId ? "admin" : "outsider");
}
