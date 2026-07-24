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

    private readonly RecordingLogger _logger = new();
    private readonly FakeTurnSource _turns = new();
    private readonly FakeUsageAlertService _usageAlerts = new();
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
    public async Task WithoutAnOpenRouterKey_TheBotStaysInert_AndSpawnsNoThread()
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
        // The creator came off the cached channel — no API round-trip needed.
        Assert.Equal(0, _gateway.ResolveCalls);
        Assert.Null(_gateway.CreatedThreadName);
    }

    [Fact]
    public async Task AThreadSomeoneElseStarted_IsIgnored()
    {
        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: HumanId, channelId: OtherChannelId), BotId, _gateway);

        AssertNoTurn();
    }

    [Fact]
    public async Task AThreadWithoutCachedCreatorMetadata_IsResolvedFromTheApi()
    {
        _gateway.ThreadCreatorId = BotId;
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("jasne"));

        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: null, channelId: OtherChannelId), BotId, _gateway);

        Assert.Equal(1, _gateway.ResolveCalls);
        Assert.Equal(["jasne"], _origin.Sent);
    }

    [Fact]
    public async Task AnUnresolvableThreadCreator_IsTreatedAsNotOurs_NotAsACrash()
    {
        _gateway.ResolveFailure = new InvalidOperationException("404 unknown channel");

        await BuildFlow().HandleAsync(
            Message(isThread: true, threadCreatorId: null, channelId: OtherChannelId), BotId, _gateway);

        AssertNoTurn();
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug);
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
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Sprawdzam."));
        _turns.Updates.Add(new ConversationUpdate.ToolBatchSummary("-# 🔧 meme_search"));
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Zna"));
        _turns.Updates.Add(new ConversationUpdate.RoundReset());
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Znalazłem trzy."));

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal(["Sprawdzam.\n-# 🔧 meme_search", "Znalazłem trzy."], _origin.Sent);
    }

    [Fact]
    public async Task ATimedOutTurn_StillPostsWhatTheModelHadStreamed()
    {
        _turns.Updates.Add(new ConversationUpdate.AssistantTextDelta("Zacząłem odpowiadać"));
        _turns.HangUntilCancelled = true;

        await BuildFlow(timeoutSeconds: 1).HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal(["Zacząłem odpowiadać"], _origin.Sent);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        // Timeouts still burn tokens, so the cap check must run.
        Assert.Equal([HumanId], _usageAlerts.Checked);
    }

    [Fact]
    public async Task AFailedTurn_IsLoggedNotThrown_AndStillRunsTheCostCapCheck()
    {
        _turns.Failure = new InvalidOperationException("model exploded");

        await BuildFlow().HandleAsync(Message(guildId: null), BotId, _gateway);

        Assert.Equal([HumanId], _usageAlerts.Checked);
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
        Assert.Equal(expected, ConversationFlow.BuildThreadName(content, BotId));
    }

    [Fact]
    public void ThreadName_IsTrimmedUnderDiscordsCap()
    {
        var name = ConversationFlow.BuildThreadName(new string('x', 200), BotId);

        Assert.Equal(90, name.Length);
    }

    private ConversationFlow BuildFlow(int timeoutSeconds = 30) =>
        new(_turns, _usageAlerts, Options.Create(new ConversationOptions
        {
            AdminUserIds = [AdminId],
            ChannelAllowList = [AllowedChannelId],
            RequestTimeoutSeconds = timeoutSeconds,
        }), _logger.For<ConversationFlow>());

    private static IncomingConversationMessage Message(
        ulong authorId = HumanId,
        bool authorIsBot = false,
        ulong? guildId = GuildId,
        ulong channelId = AllowedChannelId,
        bool mentionsBot = false,
        bool isThread = false,
        ulong? threadCreatorId = null,
        string content = "cześć") =>
        new(
            MessageId: 7000UL,
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
