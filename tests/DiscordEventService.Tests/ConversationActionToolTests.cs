using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The §6 admin gate is a crisp, security-sensitive contract, so it is exercised directly here
// without Discord: the gate short-circuits before any read service is touched, so the registry
// can be built with null read services. These cover the whole decision tree — non-admin refused,
// admin + reversible runs immediately, admin + irreversible stages (deferred) — plus the
// confirm-store primitives (double-click guard, foreign custom ids) the click handler leans on.
public sealed class ConversationActionToolTests
{
    private const ulong GuildId = 100UL;
    private const ulong InvokerId = 42UL;
    private const ulong ChannelId = 7UL;

    [Fact]
    public async Task NonAdmin_IrreversibleAction_IsRefusedAndNeverStaged()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        var toolset = BuildToolset(Context(isAdmin: false), actions, confirmations);

        var result = await InvokeAsync(toolset, "grant_role",
            new() { ["userId"] = "5", ["roleId"] = "6" });

        Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(confirmations.Staged);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task NonAdmin_ReversibleAction_IsRefusedAndNeverRuns()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        var toolset = BuildToolset(Context(isAdmin: false), actions, confirmations);

        var result = await InvokeAsync(toolset, "add_reaction",
            new() { ["channelId"] = "10", ["messageId"] = "11", ["emoji"] = "👍" });

        Assert.Contains("admin", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Admin_ReversibleAction_RunsImmediatelyWithoutStaging()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        var toolset = BuildToolset(Context(isAdmin: true), actions, confirmations);

        await InvokeAsync(toolset, "add_reaction",
            new() { ["channelId"] = "10", ["messageId"] = "11", ["emoji"] = "👍" });

        var call = Assert.Single(actions.Calls);
        Assert.Equal("AddReaction", call.Op);
        Assert.Equal(10UL, call.A);
        Assert.Equal(11UL, call.B);
        Assert.Empty(confirmations.Staged);
    }

    [Fact]
    public async Task Admin_IrreversibleAction_StagesAndDefersExecution()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        var toolset = BuildToolset(Context(isAdmin: true), actions, confirmations);

        var result = await InvokeAsync(toolset, "grant_role",
            new() { ["userId"] = "5", ["roleId"] = "6" });

        // Staged, not done — the action must not have run yet.
        var staged = Assert.Single(confirmations.Staged);
        Assert.Empty(actions.Calls);
        Assert.Contains("confirm", result, StringComparison.OrdinalIgnoreCase);

        // The out-of-band identity/surface is carried into the staged action, never from the model.
        Assert.Equal(InvokerId, staged.RequesterId);
        Assert.Equal(ChannelId, staged.ChannelId);

        // Running the staged delegate (what the confirm click does) finally performs the action.
        var outcome = await staged.Execute(CancellationToken.None);
        var call = Assert.Single(actions.Calls);
        Assert.Equal("GrantRole", call.Op);
        Assert.Equal(GuildId, call.A);
        Assert.Equal(5UL, call.B);
        Assert.Equal(6UL, call.C);
        Assert.Equal(actions.Result, outcome);
    }

    [Fact]
    public async Task Admin_IrreversibleAction_InDmWithoutPrimaryGuild_ReportsNoGuild()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        // DM (no ambient guild) + no PrimaryGuildId configured.
        var context = new ConversationContext(
            GuildId: null, InvokerId, "tester", IsAdmin: true, ChannelId);
        var toolset = BuildToolset(context, actions, confirmations, primaryGuildId: null);

        var result = await InvokeAsync(toolset, "ban_member",
            new() { ["userId"] = "5", ["reason"] = "spam" });

        Assert.Contains("server", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(confirmations.Staged);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task Admin_Timeout_ClampsMinutesConsistentlyInPromptAndExecution()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        var toolset = BuildToolset(Context(isAdmin: true), actions, confirmations);

        await InvokeAsync(toolset, "timeout_member",
            new() { ["userId"] = "5", ["minutes"] = 999999, ["reason"] = "spam" });

        const int maxMinutes = 28 * 24 * 60; // Discord's 28-day cap = 40320
        // The duration in the confirm prompt must equal what executes — not the unclamped input.
        var staged = Assert.Single(confirmations.Staged);
        Assert.Contains(maxMinutes.ToString(), staged.Description);

        await staged.Execute(CancellationToken.None);
        var call = Assert.Single(actions.Calls);
        Assert.Equal("Timeout", call.Op);
        Assert.Equal((ulong)maxMinutes, call.C);
    }

    [Fact]
    public async Task Admin_InvalidSnowflake_IsRejectedBeforeStaging()
    {
        var actions = new FakeGuildActionService();
        var confirmations = new FakeConfirmationService();
        var toolset = BuildToolset(Context(isAdmin: true), actions, confirmations);

        var result = await InvokeAsync(toolset, "grant_role",
            new() { ["userId"] = "not-a-number", ["roleId"] = "6" });

        Assert.Contains("valid", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(confirmations.Staged);
    }

    private static ConversationToolset BuildToolset(
        ConversationContext context, FakeGuildActionService actions, FakeConfirmationService confirmations,
        ulong? primaryGuildId = GuildId)
    {
        var options = Options.Create(new ConversationOptions { PrimaryGuildId = primaryGuildId });
        var registry = new ConversationToolRegistry(
            memeSearch: null!,
            guildStats: null!,
            databaseQuery: null!,
            actions,
            confirmations,
            new DatabaseSchemaHint("schema"),
            options,
            NullLogger<ConversationToolRegistry>.Instance);
        return registry.BuildToolset(context);
    }

    private static ConversationContext Context(bool isAdmin) =>
        new(GuildId, InvokerId, "tester", isAdmin, ChannelId);

    private static async Task<string> InvokeAsync(
        ConversationToolset toolset, string tool, Dictionary<string, object?> arguments)
    {
        var result = await toolset.InvokeAsync(
            new FunctionCallContent("call_1", tool, arguments), CancellationToken.None);
        return result.Result?.ToString() ?? string.Empty;
    }
}

// Records calls instead of touching Discord; shared with ConversationLoopTests (read-tool focused),
// where the action tools are never invoked.
internal sealed class FakeGuildActionService : IGuildActionService
{
    public sealed record Call(string Op, ulong A, ulong B, ulong C);

    public List<Call> Calls { get; } = [];
    public string Result { get; set; } = "done";

    public Task<string> AddReactionAsync(ulong channelId, ulong messageId, string emoji, CancellationToken ct)
    {
        Calls.Add(new Call("AddReaction", channelId, messageId, 0));
        return Task.FromResult(Result);
    }

    public Task<string> PinMessageAsync(ulong channelId, ulong messageId, CancellationToken ct)
    {
        Calls.Add(new Call("Pin", channelId, messageId, 0));
        return Task.FromResult(Result);
    }

    public Task<string> GrantRoleAsync(ulong guildId, ulong userId, ulong roleId, string reason, CancellationToken ct)
    {
        Calls.Add(new Call("GrantRole", guildId, userId, roleId));
        return Task.FromResult(Result);
    }

    public Task<string> RemoveRoleAsync(ulong guildId, ulong userId, ulong roleId, string reason, CancellationToken ct)
    {
        Calls.Add(new Call("RemoveRole", guildId, userId, roleId));
        return Task.FromResult(Result);
    }

    public Task<string> TimeoutMemberAsync(ulong guildId, ulong userId, int minutes, string reason, CancellationToken ct)
    {
        Calls.Add(new Call("Timeout", guildId, userId, (ulong)minutes));
        return Task.FromResult(Result);
    }

    public Task<string> KickMemberAsync(ulong guildId, ulong userId, string reason, CancellationToken ct)
    {
        Calls.Add(new Call("Kick", guildId, userId, 0));
        return Task.FromResult(Result);
    }

    public Task<string> BanMemberAsync(ulong guildId, ulong userId, string reason, CancellationToken ct)
    {
        Calls.Add(new Call("Ban", guildId, userId, 0));
        return Task.FromResult(Result);
    }

    public Task<string> DeleteMessageAsync(ulong channelId, ulong messageId, string reason, CancellationToken ct)
    {
        Calls.Add(new Call("Delete", channelId, messageId, 0));
        return Task.FromResult(Result);
    }

    public Task<string> DescribeUserAsync(ulong guildId, ulong userId, CancellationToken ct) =>
        Task.FromResult($"user {userId}");

    public Task<string> DescribeRoleAsync(ulong guildId, ulong roleId, CancellationToken ct) =>
        Task.FromResult($"role {roleId}");
}

internal sealed class FakeConfirmationService : IConfirmationService
{
    public sealed record StagedAction(
        ulong ChannelId, ulong RequesterId, string RequesterName, string Description,
        Func<CancellationToken, Task<string>> Execute);

    public List<StagedAction> Staged { get; } = [];
    public string StageResult { get; set; } = "I've posted a confirmation — an admin must click Confirm.";

    public Task<string> StageAsync(
        ulong channelId, ulong requesterId, string requesterName, string description,
        Func<CancellationToken, Task<string>> execute, CancellationToken cancellationToken)
    {
        Staged.Add(new StagedAction(channelId, requesterId, requesterName, description, execute));
        return Task.FromResult(StageResult);
    }

    public bool TryClaim(string token, out PendingGuildAction action)
    {
        action = null!;
        return false;
    }
}
