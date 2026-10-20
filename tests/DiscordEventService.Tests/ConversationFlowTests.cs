using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The §1 routing rules and the turn body, reachable without a Discord client or a chat
// client now that they sit behind the interaction port (#308): who gets a reply, where it
// lands, and the guarantees that hold on the unhappy paths (a timed-out turn still posts
// what it had; the post-turn cost-cap check runs on every exit).
public sealed class ConversationFlowTests
{
    private const ulong BotId = 1UL;
    private const ulong HumanId = 2UL;
    private const ulong AdminId = 3UL;
    private const ulong AllowedChannelId = 50UL;
    private const ulong OtherChannelId = 51UL;
    private const ulong GuildId = 900UL;

    private static readonly ConversationOptions Defaults = new();

    private readonly RecordingLogger _logger = new();
    private readonly FakeTurnSource _turns = new();
    private readonly FakeUsageAlertService _usageAlerts = new();
    private readonly ConversationTurnGate _turnGate = new();
    private readonly FakeTurnSurface _origin = new(AllowedChannelId);
    private readonly FakeConversationGateway _gateway;

    public ConversationFlowTests() => _gateway = new FakeConversationGateway(_origin);

    [Fact]
    public async Task TheBotsOwnMessage_IsIgnored_SoAnInThreadReplyCannotLoop()
    {
        await BuildFlow().HandleAsync(Message(authorId: BotId, isThread: true, threadCreatorId: BotId), BotId, _gateway);

        AssertNoTurn();
    }

    [Fact]
    public async Task AnotherBotsMessage_IsIgnored()
    {
        await BuildFlow().HandleAsync(Message(authorIsBot: true, mentionsBot: true), BotId, _gateway);

        AssertNoTurn();
    }

    [Fact]
    public async Task WithoutAClaudeApiKey_TheBotStaysInert_AndSpawnsNoThread()
    {
        _turns.IsConfigured = false;

        await BuildFlow().HandleAsync(Message(mentionsBot: true), BotId, _gateway);

        AssertNoTurn();
        Assert.Null(_gateway.CreatedThreadName);
        Assert.Equal(0, _origin.TypingCount);
    }

    [Fact]
    public async Task ADirectMessage_IsAnsweredInline_WithNoMentionAndNoThread()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("cześć"));

        // A DM has no guild id and no allow-list entry — the inversion vs the ingestion handlers.
        await BuildFlow().HandleAsync(Message(guildId: null, channelId: OtherChannelId), BotId, _gateway);

        Assert.Equal(["cześć"], _origin.Sent);
        Assert.Null(_gateway.CreatedThreadName);
        Assert.Null(_turns.Turns.Single().Context.GuildId);
    }

    [Fact]
    public async Task APlainGuildMessage_WithoutAMention_IsIgnored()
    {
        await BuildFlow().HandleAsync(Message(), BotId, _gateway);

        AssertNoTurn();
    }

    [Fact]
    public async Task AMention_OutsideTheAllowList_IsIgnored()
    {
        await BuildFlow().HandleAsync(Message(mentionsBot: true, channelId: OtherChannelId), BotId, _gateway);

        AssertNoTurn();
        Assert.Null(_gateway.CreatedThreadName);
    }

    [Fact]
    public async Task AMention_InAnAllowListedChannel_SpawnsAThread_AndRepliesThere()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("już patrzę"));

        await BuildFlow().HandleAsync(
            Message(mentionsBot: true, content: $"<@{BotId}> ile mamy memów?"), BotId, _gateway);

        Assert.Equal("ile mamy memów?", _gateway.CreatedThreadName);
        Assert.Equal(["już patrzę"], _gateway.CreatedThread!.Sent);
        Assert.Empty(_origin.Sent);
        // The staged-action surface is the thread, not the channel the mention came from.
        Assert.Equal(_gateway.CreatedThread.ChannelId, _turns.Turns.Single().Context.ChannelId);
    }

    [Fact]
    public async Task AFollowUpInABotOwnedThread_NeedsNoReMention()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("jasne"));

        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: BotId, channelId: OtherChannelId), BotId, _gateway);

        Assert.Equal(["jasne"], _origin.Sent);
        // The creator came off the cached channel — no store lookup needed.
        Assert.Equal(0, _turns.StoreLookups);
        Assert.Null(_gateway.CreatedThreadName);
    }

    [Fact]
    public async Task AThreadSomeoneElseStarted_IsIgnored()
    {
        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: HumanId, channelId: OtherChannelId), BotId, _gateway);

        AssertNoTurn();
    }

    // #454: DSharpPlus hands an uncached thread over as a stub Text channel, so neither the
    // thread flag nor the creator can be trusted — the conversation store decides.
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 0UL)]
    public async Task AThreadTheCacheCantVouchFor_IsOurs_WhenTheStoreHoldsItsConversation(bool isThread, ulong? creatorId)
    {
        _turns.Conversations.Add(OtherChannelId);
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("jasne"));

        await BuildFlow().HandleAsync(
            Message(isThread: isThread, threadCreatorId: creatorId, channelId: OtherChannelId), BotId, _gateway);

        Assert.Equal(["jasne"], _origin.Sent);
        Assert.Null(_gateway.CreatedThreadName);
    }

    [Fact]
    public async Task AThreadTheCacheCantVouchFor_IsIgnored_WhenTheStoreDoesNotKnowIt()
    {
        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: 0UL, channelId: OtherChannelId), BotId, _gateway);

        Assert.Equal(1, _turns.StoreLookups);
        AssertNoTurn();
    }

    [Fact]
    public async Task AnUnreadableStore_IsTreatedAsNotOurs_NotAsACrash()
    {
        _turns.StoreFailure = new InvalidOperationException("db down");

        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: null, channelId: OtherChannelId), BotId, _gateway);

        AssertNoTurn();
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug);
    }

    // #454: a human's rename of a bot-owned thread, a pin notice or "added to thread" carries
    // a human author — before the fix the rename started a turn with the new name as prompt.
    [Fact]
    public async Task ASystemMessage_InABotOwnedThread_StartsNoTurn()
    {
        await BuildFlow().HandleAsync(
            Message(isUserMessage: false, isThread: true, threadCreatorId: BotId, channelId: OtherChannelId,
                content: "ile jest emoji na serwerze"),
            BotId, _gateway);

        AssertNoTurn();
    }

    [Fact]
    public async Task ASystemMessage_InADirectMessage_StartsNoTurn()
    {
        await BuildFlow().HandleAsync(Message(isUserMessage: false, guildId: null, content: ""), BotId, _gateway);

        AssertNoTurn();
    }

    [Fact]
    public async Task TheInvocationContext_IsCapturedFromTheEvent_NeverFromTheModel()
    {
        await BuildFlow().HandleAsync(
            Message(authorId: AdminId, guildId: GuildId, mentionsBot: true, content: "zrób coś"), BotId, _gateway);

        var context = _turns.Turns.Single().Context;
        Assert.Equal(GuildId, context.GuildId);
        Assert.Equal(AdminId, context.InvokerId);
        Assert.Equal("Kowal", context.InvokerDisplayName);
        // The §6 action gate — from the allow-list, never a model parameter.
        Assert.True(context.IsAdmin);
    }

    [Fact]
    public async Task ANonAdminInvoker_IsNotGrantedTheActionGate()
    {
        await BuildFlow().HandleAsync(Message(mentionsBot: true), BotId, _gateway);

        Assert.False(_turns.Turns.Single().Context.IsAdmin);
    }

    [Fact]
    public async Task RenderEvents_MapToDiscreteMessages_PerRound()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Szukam w sieci."));
        _turns.Updates.Add(new ConversationUpdate.ToolCall("-# 🔎 web_search: żółw"));
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Sprawdzam memy."));
        _turns.Updates.Add(new ConversationUpdate.ToolCall("-# 🔧 meme_search"));
        _turns.Updates.Add(new ConversationUpdate.ToolCall("-# 🔧 meme_search"));
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Zna"));
        _turns.Updates.Add(new ConversationUpdate.RoundReset("Zna".Length));
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Znalazłem trzy."));

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal(["Szukam w sieci.", "-# 🔎 web_search: żółw", "Sprawdzam memy.",
            "-# 🔧 meme_search", "-# 🔧 meme_search", "Znalazłem trzy."], _origin.Sent);
    }

    [Fact]
    public async Task ATimedOutTurn_StillPostsWhatTheModelHadStreamed()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Zacząłem odpowiadać"));
        _turns.HangUntilCancelled = true;

        await BuildFlow(timeoutSeconds: 1).HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal(["Zacząłem odpowiadać", Defaults.TimeoutMessage], _origin.Sent);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        // Timeouts still burn tokens, so the cap check must run.
        Assert.Equal([HumanId], _usageAlerts.Checked);
    }

    // #445: a hang during thinking, a tool or a backoff streamed nothing; silence is not an answer.
    [Fact]
    public async Task ATimedOutTurn_WithNothingStreamed_PostsTheTimeoutLine()
    {
        _turns.HangUntilCancelled = true;

        await BuildFlow(timeoutSeconds: 1).HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal([Defaults.TimeoutMessage], _origin.Sent);
    }

    [Fact]
    public async Task AFailedTurn_PostsTheFailureLine_IsLoggedNotThrown_AndStillRunsTheCostCapCheck()
    {
        _turns.Failure = new InvalidOperationException("model exploded");

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal([Defaults.FailureMessage], _origin.Sent);
        Assert.Equal([HumanId], _usageAlerts.Checked);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AFailedTurn_AfterADelta_PostsTheDeltaThenTheFailureLine()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Zacząłem"));
        _turns.Failure = new InvalidOperationException("db down");

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal(["Zacząłem", Defaults.FailureMessage], _origin.Sent);
    }

    // A failed chunk send stops the answer: a gapped answer must not read as complete.
    [Fact]
    public async Task AFailedSend_StopsTheTurn_AndPostsTheFailureLine()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Szukam."));
        _turns.Updates.Add(new ConversationUpdate.ToolCall("-# 🔧 meme_search"));
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Odpowiedź."));
        _origin.FailingSend = "Szukam.";

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal([Defaults.FailureMessage], _origin.Sent);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AThrowingTypingIndicator_StillPostsTheAnswer()
    {
        _origin.TypingFailure = new InvalidOperationException("Discord 429");
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("gotowe"));

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal(["gotowe"], _origin.Sent);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task AFailedThreadCreation_RepliesInlineWithTheFailureLine_AndRunsNoTurn()
    {
        _gateway.CreateThreadFailure = new InvalidOperationException("Discord 403");

        await BuildFlow().HandleAsync(Message(mentionsBot: true), BotId, _gateway);

        Assert.Equal([Defaults.FailureMessage], _origin.Sent);
        Assert.Empty(_turns.Turns);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ASuccessfulTurn_RunsTheCostCapCheckToo()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("gotowe"));

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal([HumanId], _usageAlerts.Checked);
    }

    [Theory]
    [InlineData("<@1> ile mamy memów?", "ile mamy memów?")]
    [InlineData("<@!1> ile mamy memów?", "ile mamy memów?")]
    [InlineData("<@1>", "Chat with Wojtuś")]
    [InlineData("   ", "Chat with Wojtuś")]
    public void ThreadName_StripsTheMention_AndFallsBackWhenNothingIsLeft(string content, string expected)
    {
        Assert.Equal(expected, ConversationFlow.BuildThreadName(content));
    }

    [Theory]
    [InlineData("<@1> zapytaj <@123> i <@&55> o <#77>", "zapytaj i o")]
    [InlineData("<@1> co to\nza   <:pepe:999> i <a:dance:1>?", "co to za :pepe: i :dance:?")]
    [InlineData("<@1> spotkanie <t:1700000000:R>, użyj </meme:42>", "spotkanie , użyj")]
    public void ThreadName_DropsDiscordTokens_AndCollapsesWhitespace(string content, string expected)
    {
        Assert.Equal(expected, ConversationFlow.BuildThreadName(content));
    }

    [Fact]
    public void ThreadName_CutsOnATextElementBoundary()
    {
        // An emoji (a surrogate pair) straddling the 90-char cap: never half of it (U+FFFD).
        var name = ConversationFlow.BuildThreadName($"<@1> {new string('x', 89)}😀 dalej");

        Assert.Equal(new string('x', 89), name);
    }

    [Fact]
    public void ThreadName_IsTrimmedUnderDiscordsCap()
    {
        var name = ConversationFlow.BuildThreadName(new string('x', 200));

        Assert.Equal(90, name.Length);
    }

    [Fact]
    public async Task TwoMessagesInOneChannel_RunOneTurnAfterTheOther()
    {
        _turns.HoldFirstTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Two scoped flows over one gate, as DSharpPlus dispatches two events (#449). The fakes
        // complete synchronously, so without the gate the second turn would start right here.
        var first = BuildFlow().HandleAsync(Message(guildId: null, content: "pierwsza"), BotId, _gateway);
        var second = BuildFlow().HandleAsync(Message(guildId: null, content: "druga"), BotId, _gateway);

        Assert.Equal(["pierwsza"], _turns.Turns.Select(t => t.Message));

        _turns.HoldFirstTurn.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["pierwsza", "druga"], _turns.Turns.Select(t => t.Message));
    }

    [Fact]
    public async Task ATurnInAnotherChannel_DoesNotWait()
    {
        _turns.HoldFirstTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherGateway = new FakeConversationGateway(new FakeTurnSurface(OtherChannelId));

        var first = BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);
        await BuildFlow().HandleAsync(Message(guildId: null, channelId: OtherChannelId), BotId, otherGateway)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, _turns.Turns.Count);
        _turns.HoldFirstTurn.SetResult();
        await first;
    }

    private ConversationFlow BuildFlow(int timeoutSeconds = 30) =>
        new(_turns, _usageAlerts, _turnGate, Options.Create(new ConversationOptions
        {
            AdminUserIds = [AdminId],
            ChannelAllowList = [AllowedChannelId],
            RequestTimeoutSeconds = timeoutSeconds,
        }), _logger.For<ConversationFlow>());

    private static IncomingConversationMessage Message(
        ulong authorId = HumanId,
        bool authorIsBot = false,
        bool isUserMessage = true,
        ulong? guildId = GuildId,
        ulong channelId = AllowedChannelId,
        bool mentionsBot = false,
        bool isThread = false,
        ulong? threadCreatorId = null,
        string content = "cześć") =>
        new(
            MessageId: 7000UL,
            IsUserMessage: isUserMessage,
            ChannelId: channelId,
            GuildId: guildId,
            AuthorId: authorId,
            AuthorIsBot: authorIsBot,
            AuthorDisplayName: "Kowal",
            Content: content,
            ChannelIsThread: isThread,
            CachedThreadCreatorId: threadCreatorId,
            MentionedUserIds: mentionsBot ? [BotId] : []);

    private void AssertNoTurn()
    {
        Assert.Empty(_turns.Turns);
        Assert.Empty(_origin.Sent);
        Assert.Empty(_usageAlerts.Checked);
    }
}
