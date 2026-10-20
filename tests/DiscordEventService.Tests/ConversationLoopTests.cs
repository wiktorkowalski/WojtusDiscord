using System.Diagnostics;
using System.Runtime.CompilerServices;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Conversations;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// Exercises the §3 streaming contract end-to-end against a real Postgres + real
// MemeSearchService, with the model itself faked by a scripted streaming IChatClient.
// Proves the model->tool->model loop AND its render events: text deltas yielded as they
// stream, one tool-call line per call yielded before that tool runs (#439, #456; no injected
// canned cue — the model's own narration is the progress note, #274), the tool result fed back into
// the next model call, a final answer accumulated from deltas, and the round cap
// terminating the loop with tool calls forbidden; plus the Claude API loop shapes (#426):
// grouped tool results, pause_turn continuation, a refusal.
public sealed class ConversationLoopTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;
    private const ulong MemeMessageId = 1301UL;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;
    private MemeSearchLogWriter _searchLog = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeSearchLog.ExecuteDeleteAsync();
        await _db.MemeAnnotations.ExecuteDeleteAsync();
        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        // #267: the loop now persists into the conversation store; without cleanup a prior
        // test's history replays into the next test's transcript.
        await _db.ConversationMessages.ExecuteDeleteAsync();
        await _db.ConversationUsage.ExecuteDeleteAsync();
        await _db.Conversations.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        _db.Guilds.Add(_guild);
        await _db.SaveChangesAsync();

        _channel = new ChannelEntity
        {
            DiscordId = ChannelDiscordId,
            GuildId = _guild.Id,
            Name = "memes",
            Type = ChannelType.Text
        };
        _author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(_channel);
        _db.Users.Add(_author);
        await _db.SaveChangesAsync();

        await SeedTurtleMemeAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task GenerateReplyAsync_SilentToolCallThenAnswer_SummaryAloneFeedsResultBackAndStreamsAnswer()
    {
        const string finalAnswer = "Found the turtle meme for you.";
        // Round 0 calls the tool with no narration text; round 1 streams the answer in two
        // deltas (to prove accumulation).
        var client = ScriptToolThenAnswer("Found the turtle ", "meme for you.");

        var service = BuildService(client);
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        var events = await CollectAsync(service.GenerateReplyAsync("znajdz mema o zolwiu", context, CancellationToken.None));

        // The model was silent before the tool, and the loop no longer injects a canned
        // cue (#274) — the tool-call lines are the round's only render event.
        var call = Assert.Single(events.OfType<ConversationUpdate.ToolCall>());
        Assert.Equal("-# 🔧 meme_search", call.Line);
        Assert.True(IndexOf<ConversationUpdate.AssistantTextDelta>(events) > IndexOf<ConversationUpdate.ToolCall>(events),
            "a silent tool round should yield no text before its tool calls");
        // The answer is the deltas after the summary, accumulated into one message.
        Assert.Equal(finalAnswer, await TurnRendering.FinalAnswerAsync(events));

        // Two model calls: the tool-requesting round, then the answering round.
        Assert.Equal(2, client.Calls.Count);
        // Tools are re-sent on every round.
        Assert.Equal([true, true], client.CallHadTools);

        // The second call's transcript carries the tool result, and the assistant tool-call
        // turn precedes it.
        var secondCall = client.Calls[1];
        var toolCallIndex = IndexOfContent<FunctionCallContent>(secondCall);
        var toolResultIndex = IndexOfContent<FunctionResultContent>(secondCall);
        var toolResult = secondCall
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .SingleOrDefault();

        Assert.True(toolCallIndex >= 0, "assistant tool-call turn should be in the replayed transcript");
        Assert.NotNull(toolResult);
        Assert.True(toolCallIndex < toolResultIndex, "tool-call turn must precede the tool result");
        // The result the model sees came from the real search — the seeded meme's jump link.
        Assert.Contains($"discord.com/channels/{GuildDiscordId}/{ChannelDiscordId}/{MemeMessageId}",
            toolResult!.Result?.ToString());

        // #384: the tool's search is logged under the person who asked and the conversation's
        // channel — both from the out-of-band context, not from the model's arguments.
        await _searchLog.LastWrite;
        await using var db = NewContext();
        var logged = await db.MemeSearchLog.Include(s => s.Results).SingleAsync();
        Assert.Equal(
            (MemeSearchSource.AssistantTool, GuildDiscordId, 7UL, 42UL, "zolw", 1),
            (logged.Source, logged.GuildDiscordId, logged.ChannelDiscordId, logged.UserDiscordId, logged.Query, logged.ResultCount));
        Assert.Single(logged.Results);
    }

    [Fact]
    public async Task GenerateReplyAsync_ToolCallLine_IsYieldedBeforeTheToolRuns()
    {
        // #456: the renderer posts the narration on the ToolCall event, so the event must
        // arrive while the tool has not run yet — else a slow query shows its note after it.
        var service = BuildService(ScriptToolThenAnswer("gotowe"));
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        var toolRowsAtToolCall = -1;
        await foreach (var update in service.GenerateReplyAsync("memy?", context, CancellationToken.None))
        {
            if (update is ConversationUpdate.ToolCall)
                toolRowsAtToolCall = await CountToolRowsAsync();
        }

        // The tool's persisted result row is written only after the tool ran.
        Assert.Equal(0, toolRowsAtToolCall);
        Assert.Equal(1, await CountToolRowsAsync());

        async Task<int> CountToolRowsAsync()
        {
            await using var db = NewContext();
            return await db.ConversationMessages.CountAsync(m => m.Role == ConversationMessageRole.Tool);
        }
    }

    [Fact]
    public async Task GenerateReplyAsync_ModelNarratesBeforeTool_StreamsNarrationThenSummaryThenAnswer()
    {
        // A single round emits narration text AND a tool call (the realistic shape); the
        // next round streams the answer across deltas.
        var client = new ScriptedChatClient((callIndex, _, _) => callIndex switch
        {
            0 =>
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Sprawdzam memy. "),
                ToolCallUpdate("call_1", "meme_search",
                    new Dictionary<string, object?> { ["query"] = "zolw", ["limit"] = 5 }),
            ],
            _ => StreamText("Oto ", "co ", "znalazłem."),
        });

        var service = BuildService(client);
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        var events = await CollectAsync(service.GenerateReplyAsync("memy?", context, CancellationToken.None));

        // The model's own narration is surfaced and precedes the tool-call lines.
        Assert.Equal("Sprawdzam memy. ", FirstDelta(events));
        Assert.True(IndexOf<ConversationUpdate.AssistantTextDelta>(events) < IndexOf<ConversationUpdate.ToolCall>(events));
        Assert.Single(events.OfType<ConversationUpdate.ToolCall>());
        // The answer is accumulated from its deltas.
        Assert.Equal("Oto co znalazłem.", await TurnRendering.FinalAnswerAsync(events));
    }

    [Fact]
    public async Task GenerateReplyAsync_TwoCallsOfOneToolInARound_YieldOneLineEach()
    {
        // #438: two meme_search calls are two messages, in the model's order — no Distinct.
        var client = new ScriptedChatClient((callIndex, _, _) => callIndex switch
        {
            0 =>
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Szukam dwóch. "),
                ToolCallUpdate("call_1", "meme_search",
                    new Dictionary<string, object?> { ["query"] = "zolw", ["limit"] = 5 }),
                ToolCallUpdate("call_2", "meme_search",
                    new Dictionary<string, object?> { ["query"] = "kot", ["limit"] = 5 }),
            ],
            _ => StreamText("Gotowe."),
        });

        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);
        var events = await CollectAsync(BuildService(client).GenerateReplyAsync("memy?", context, CancellationToken.None));

        Assert.Equal(["-# 🔧 meme_search", "-# 🔧 meme_search"],
            events.OfType<ConversationUpdate.ToolCall>().Select(call => call.Line));
        Assert.Equal("Gotowe.", await TurnRendering.FinalAnswerAsync(events));
    }

    [Fact]
    public async Task GenerateReplyAsync_ModelNeverStops_HitsRoundCapAndFinalizesWithoutTools()
    {
        const int cap = 3;
        const string cappedAnswer = "Okay, stopping here.";
        // Always ask for a tool while calls are allowed; answer once tool_choice is none.
        var client = new ScriptedChatClient((callIndex, _, options) =>
            options?.ToolMode is not NoneChatToolMode
                ? StreamToolCall($"call_{callIndex}", "meme_search",
                    new Dictionary<string, object?> { ["query"] = "x", ["limit"] = 1 })
                : StreamText(cappedAnswer));

        var service = BuildService(client, maxToolRounds: cap);
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        var events = await CollectAsync(service.GenerateReplyAsync("loop forever", context, CancellationToken.None));

        Assert.Equal(cappedAnswer, await TurnRendering.FinalAnswerAsync(events));
        // cap rounds allowing tool calls, then one finalizing call that forbids them. The
        // tools stay declared on it: the Claude API rejects tool_use history with no tools.
        Assert.Equal(cap + 1, client.Calls.Count);
        Assert.Equal([true, true, true, true], client.CallHadTools);
        Assert.Equal([false, false, false, true], client.CallForbadeTools);
    }

    [Fact]
    public async Task GenerateReplyAsync_CappedTurn_NestsEveryDbSpanUnderTheTurn_AndTagsItsRounds()
    {
        // #458: Tempo drops a DB span with no parent, so every write of the turn — the user
        // message before round 1, each round's ledger and transcript rows — must sit under the
        // turn span. Only this class's database counts: other test classes run in parallel.
        var database = new Npgsql.NpgsqlConnectionStringBuilder(fixture.ConnectionString).Database;
        Activity? turn = null;
        List<Activity> dbSpans = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Npgsql" || source.Name == ConversationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            // Read on stop: Npgsql sets its connection tags after the start, and the turn's
            // channel tells this test's turn from a parallel class's one.
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "conversation.turn" && (string?)activity.GetTagItem("session.id") == "456")
                    turn = activity;
                else if (activity.Source.Name == "Npgsql" && activity.TagObjects.Any(tag => tag.Value?.ToString() == database))
                    lock (dbSpans)
                        dbSpans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        var client = new ScriptedChatClient((callIndex, _, options) =>
            options?.ToolMode is not NoneChatToolMode
                ? StreamToolCall($"call_{callIndex}", "meme_search",
                    new Dictionary<string, object?> { ["query"] = "x", ["limit"] = 1 })
                : StreamText("Stop."));
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 456UL);

        await CollectAsync(BuildService(client, maxToolRounds: 1).GenerateReplyAsync("loop", context, CancellationToken.None));

        Assert.NotNull(turn);
        Assert.Equal(2, turn.GetTagItem("conversation.rounds"));
        Assert.NotEmpty(dbSpans);
        lock (dbSpans)
            Assert.All(dbSpans, span => Assert.Equal(turn.TraceId, span.TraceId));
    }

    // #459: a post-cap round that answers with no text still ends the turn with a line.
    [Fact]
    public async Task GenerateReplyAsync_PostCapRoundWithNoText_PostsTheCapFallback()
    {
        var client = new ScriptedChatClient((callIndex, _, options) =>
            options?.ToolMode is not NoneChatToolMode
                ? StreamToolCall($"call_{callIndex}", "server_info", [])
                : [new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("nic nie wymyślę")])]);
        var log = new RecordingLogger();

        var events = await CollectAsync(BuildService(client, maxToolRounds: 1, logger: log.For<ConversationService>())
            .GenerateReplyAsync("loop", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 7UL),
                CancellationToken.None));

        Assert.Equal("Skończył mi się limit kroków, zanim zdążyłem odpowiedzieć — spróbuj zawęzić pytanie.",
            await TurnRendering.FinalAnswerAsync(events));
        Assert.StartsWith("Conversation turn round_cap: ",
            Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message);
    }

    // #459: a post-cap round that fails posts the failure line and ledgers its attempt.
    [Fact]
    public async Task GenerateReplyAsync_PostCapRoundFails_PostsTheFailureLineAndLedgersTheFailedAttempt()
    {
        var client = new ScriptedChatClient((callIndex, _, options) =>
            options?.ToolMode is not NoneChatToolMode
                ? StreamToolCall($"call_{callIndex}", "server_info", [])
                : throw new InvalidOperationException("scripted terminal failure"));
        var log = new RecordingLogger();

        var events = await CollectAsync(BuildService(client, maxToolRounds: 1, logger: log.For<ConversationService>())
            .GenerateReplyAsync("loop", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 7UL),
                CancellationToken.None));

        Assert.Equal(new ConversationOptions().FailureMessage, await TurnRendering.FinalAnswerAsync(events));
        var line = Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message;
        Assert.StartsWith("Conversation turn failed: ", line);
        Assert.Contains("rounds 2,", line);
        await using var db = NewContext();
        var rows = await db.ConversationUsage.OrderBy(u => u.Round).ThenBy(u => u.Attempt)
            .Select(u => new { u.Round, u.Attempt, u.Failed }).ToListAsync();
        Assert.Equal([(1, 1, false), (2, 1, true)], rows.Select(r => (r.Round, r.Attempt, r.Failed)));
    }

    // #459: the turn span carries the outcome the trace is filtered by, and its cost is the
    // ledger's. Each case has its own channel: the listener sees the turns of every class.
    [Theory]
    [InlineData("answered", 4591UL)]
    [InlineData("failed", 4592UL)]
    [InlineData("refused", 4593UL)]
    [InlineData("round_cap", 4594UL)]
    public async Task GenerateReplyAsync_TurnSpan_TagsTheOutcomeRoundsAndCost(string outcome, ulong channelId)
    {
        using var spans = new TurnSpanListener(channelId);
        var client = new ScriptedChatClient((callIndex, _, options) => outcome switch
        {
            "failed" => throw new InvalidOperationException("scripted terminal failure"),
            "refused" => [Billed(new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ContentFilter })],
            "round_cap" when options?.ToolMode is not NoneChatToolMode =>
                [.. StreamToolCall($"call_{callIndex}", "server_info", []), Billed()],
            _ => [.. StreamText("Odpowiedź."), Billed()],
        });

        var events = await CollectAsync(BuildService(client, maxToolRounds: 1, enableSensitiveData: true)
            .GenerateReplyAsync("pytanie", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, channelId),
                CancellationToken.None));

        var turn = spans.Turn;
        Assert.NotNull(turn);
        Assert.Equal(outcome == "failed" ? true : null, turn.GetTagItem("conversation.failed"));
        Assert.Equal(outcome == "refused" ? true : null, turn.GetTagItem("conversation.refused"));
        Assert.Equal(outcome == "round_cap" ? true : null, turn.GetTagItem("conversation.hit_round_cap"));
        Assert.Equal(outcome == "round_cap" ? 2 : 1, turn.GetTagItem("conversation.rounds"));
        Assert.Equal("pytanie", turn.GetTagItem("langfuse.trace.input"));
        Assert.Equal(await TurnRendering.FinalAnswerAsync(events), turn.GetTagItem("langfuse.trace.output"));

        await using var db = NewContext();
        var ledgerCost = (await db.ConversationUsage.Select(u => u.CostUsd).ToListAsync()).Sum(cost => cost ?? 0);
        Assert.Equal(ledgerCost, (double)turn.GetTagItem("conversation.cost_usd")!, precision: 9);
        Assert.Equal(outcome != "failed", ledgerCost > 0);
    }

    // #459: the privacy gate — with sensitive data off the trace holds no question or answer.
    [Fact]
    public async Task GenerateReplyAsync_SensitiveDataOff_TurnSpanHasNoTraceInputOrOutput()
    {
        using var spans = new TurnSpanListener(4595UL);

        await CollectAsync(BuildService(new ScriptedChatClient((_, _, _) => StreamText("Odpowiedź.")))
            .GenerateReplyAsync("pytanie", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 4595UL),
                CancellationToken.None));

        Assert.NotNull(spans.Turn);
        Assert.Null(spans.Turn.GetTagItem("langfuse.trace.input"));
        Assert.Null(spans.Turn.GetTagItem("langfuse.trace.output"));
    }

    // The usage update a real stream ends with: what the ledger and the cost tag are computed from.
    private static ChatResponseUpdate Billed(ChatResponseUpdate? update = null)
    {
        update ??= new ChatResponseUpdate(ChatRole.Assistant, []);
        update.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 100 }));
        return update;
    }

    // The turn span of one channel, read on stop when its tags are final.
    private sealed class TurnSpanListener : IDisposable
    {
        private readonly ActivityListener _listener;

        public TurnSpanListener(ulong channelId)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ConversationTelemetry.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "conversation.turn"
                        && (string?)activity.GetTagItem("session.id") == channelId.ToString())
                        Turn = activity;
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Activity? Turn { get; private set; }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task GenerateReplyAsync_TwoToolCallsInOneRound_ResultsGoBackInOneMessage()
    {
        var client = new ScriptedChatClient((callIndex, _, _) => callIndex switch
        {
            0 =>
            [
                ToolCallUpdate("call_a", "meme_search", new Dictionary<string, object?> { ["query"] = "zolw", ["limit"] = 1 }),
                ToolCallUpdate("call_b", "server_info", []),
            ],
            _ => StreamText("Gotowe."),
        });

        var events = await CollectAsync(BuildService(client).GenerateReplyAsync(
            "dwa narzędzia", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 7UL),
            CancellationToken.None));

        Assert.Equal("Gotowe.", await TurnRendering.FinalAnswerAsync(events));
        var toolMessage = Assert.Single(client.Calls[1], m => m.Role == ChatRole.Tool);
        Assert.Equal(["call_a", "call_b"], toolMessage.Contents.OfType<FunctionResultContent>().Select(r => r.CallId));
    }

    // No Claude API key: the source says it is not configured and a turn sends nothing.
    [Fact]
    public async Task GenerateReplyAsync_NoApiKey_IsNotConfiguredAndCallsNothing()
    {
        var client = ScriptToolThenAnswer("unused");
        var log = new RecordingLogger();
        var service = BuildService(client, logger: log.For<ConversationService>(), apiKey: null);

        var events = await CollectAsync(service.GenerateReplyAsync(
            "hej", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 7UL),
            CancellationToken.None));

        Assert.False(service.IsConfigured);
        Assert.Empty(events);
        Assert.Empty(client.Calls);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Anthropic:ApiKey"));
    }

    // pause_turn: the paused response is appended and the model is called again — it is not
    // the answer, and no tool runs.
    [Fact]
    public async Task GenerateReplyAsync_PauseTurn_CallsAgainAndJoinsTheAnswer()
    {
        var client = new ScriptedChatClient((callIndex, _, _) => callIndex switch
        {
            0 => [new ChatResponseUpdate(ChatRole.Assistant, "Szukam… ")
            {
                FinishReason = new ChatFinishReason(ConversationService.PauseTurnReason),
            }],
            _ => StreamText("Mam."),
        });

        var events = await CollectAsync(BuildService(client).GenerateReplyAsync(
            "szukaj", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 7UL),
            CancellationToken.None));

        Assert.Equal(2, client.Calls.Count);
        Assert.Equal("Szukam… Mam.", await TurnRendering.FinalAnswerAsync(events));
        Assert.Equal(ChatRole.Assistant, client.Calls[1][^1].Role);
        Assert.Equal("Szukam… ", client.Calls[1][^1].Text);
    }

    // A retried round after a pause: the reset drops only the failed attempt's text, so the
    // paused text stays in the renderer's buffer.
    [Fact]
    public async Task GenerateReplyAsync_PauseThenRetriedRound_KeepsThePausedText()
    {
        var client = new FailingOnceChatClient();

        var events = await CollectAsync(BuildService(client).GenerateReplyAsync(
            "szukaj", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: false, ChannelId: 7UL),
            CancellationToken.None));

        Assert.Contains(events, e => e is ConversationUpdate.RoundReset);
        Assert.Equal("Szukam… Mam.", await TurnRendering.FinalAnswerAsync(events));
    }

    // Call 1 pauses; call 2 streams text and dies; call 3 answers.
    private sealed class FailingOnceChatClient : IChatClient
    {
        private int _calls;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            switch (_calls++)
            {
                case 0:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "Szukam… ")
                    {
                        FinishReason = new ChatFinishReason(ConversationService.PauseTurnReason),
                    };
                    break;
                case 1:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "Ma");
                    throw new HttpRequestException("connection reset");
                default:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "Mam.");
                    break;
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // A refusal can carry a tool call written before the classifier stopped the model: it
    // must not run, the turn ends with a clear line, and one Warning names the round.
    [Fact]
    public async Task GenerateReplyAsync_Refusal_RunsNoToolAndSaysSo()
    {
        var client = new ScriptedChatClient((_, _, _) =>
        [
            new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("call_x", "ban_member", new Dictionary<string, object?> { ["userId"] = "1" })])
            {
                FinishReason = ChatFinishReason.ContentFilter,
            },
        ]);
        var log = new RecordingLogger();

        var events = await CollectAsync(BuildService(client, logger: log.For<ConversationService>()).GenerateReplyAsync(
            "zbanuj", new ConversationContext(GuildDiscordId, 42UL, "tester", IsAdmin: true, ChannelId: 7UL),
            CancellationToken.None));

        Assert.Single(client.Calls);
        Assert.DoesNotContain(events, e => e is ConversationUpdate.ToolCall);
        Assert.Equal(ConversationService.RefusalMessage, await TurnRendering.FinalAnswerAsync(events));
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("refused"));
        Assert.StartsWith("Conversation turn refused: ",
            Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message);
    }

    // #341: one Information line per turn. With the sensitive-data gate off (the default) it
    // names who, where, the rounds, the tool calls and the trace, and no text of the person
    // or of the model.
    [Fact]
    public async Task GenerateReplyAsync_SensitiveDataOff_TurnLogHasToolsAndTraceButNoQuestionOrAnswer()
    {
        const string question = "znajdz mema o zolwiu";
        const string answer = "Found the turtle meme for you.";
        using var spans = ListenToConversationSpans();
        var log = new RecordingLogger();
        var service = BuildService(ScriptToolThenAnswer(answer), logger: log.For<ConversationService>());
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        await CollectAsync(service.GenerateReplyAsync(question, context, CancellationToken.None));

        var line = Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message;
        Assert.StartsWith("Conversation turn answered: invoker 42 (tester), guild 1, channel 7, rounds 2, ", line);
        Assert.Contains("tools [meme_search(query=zolw, limit=5)]", line);
        // The trace id of this turn's own span. The listener is for the whole process, so it
        // also holds the turns of the tests that run at the same time.
        var traceId = System.Text.RegularExpressions.Regex.Match(line, "trace ([0-9a-f]{32}),").Groups[1].Value;
        Assert.Contains(traceId, spans.Snapshot());
        Assert.EndsWith("question [redacted], answer [redacted]", line);
        Assert.DoesNotContain(question, line);
        Assert.DoesNotContain(answer, line);
    }

    [Fact]
    public async Task GenerateReplyAsync_SensitiveDataOn_TurnLogHasQuestionAndAnswer()
    {
        // The line breaks are what a person can type to forge a log line: the turn log must
        // stay one line.
        const string question = "znajdz mema\ninfo: Forged[0]";
        const string answer = "Found the turtle meme\r\nfor you.";
        var log = new RecordingLogger();
        var service = BuildService(
            ScriptToolThenAnswer(answer), enableSensitiveData: true, logger: log.For<ConversationService>());
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "te\nster", IsAdmin: false, ChannelId: 7UL);

        await CollectAsync(service.GenerateReplyAsync(question, context, CancellationToken.None));

        var line = Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message;
        Assert.Contains("invoker 42 (te ster)", line);
        Assert.EndsWith("question znajdz mema info: Forged[0], answer Found the turtle meme for you.", line);
        Assert.DoesNotContain('\n', line);
    }

    // A turn the consumer leaves early (what a timeout does) still writes its line.
    [Fact]
    public async Task GenerateReplyAsync_ConsumerStopsEarly_TurnLogSaysAborted()
    {
        var log = new RecordingLogger();
        var service = BuildService(ScriptToolThenAnswer("unused"), logger: log.For<ConversationService>());
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        await foreach (var _ in service.GenerateReplyAsync("q", context, CancellationToken.None))
            break;

        var line = Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message;
        Assert.StartsWith("Conversation turn aborted: ", line);
    }

    private static ScriptedChatClient ScriptToolThenAnswer(params string[] answerDeltas) =>
        new((callIndex, _, _) => callIndex switch
        {
            0 => StreamToolCall("call_1", "meme_search",
                new Dictionary<string, object?> { ["query"] = "zolw", ["limit"] = 5 }),
            _ => StreamText(answerDeltas),
        });

    private static ConversationSpans ListenToConversationSpans() => new();

    // Makes the conversation source start real spans (with no listener StartActivity returns
    // null and a turn has no trace id) and keeps the trace id of each turn span.
    private sealed class ConversationSpans : IDisposable
    {
        private readonly ActivityListener _listener;

        public ConversationSpans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ConversationTelemetry.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    if (activity.OperationName == "conversation.turn")
                    {
                        lock (TurnTraceIds)
                            TurnTraceIds.Add(activity.TraceId.ToString());
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        private List<string> TurnTraceIds { get; } = [];

        public List<string> Snapshot()
        {
            lock (TurnTraceIds)
                return [.. TurnTraceIds];
        }

        public void Dispose() => _listener.Dispose();
    }

    private ConversationService BuildService(
        IChatClient client, int maxToolRounds = 8, bool enableSensitiveData = false,
        ILogger<ConversationService>? logger = null, string? apiKey = "test-key")
    {
        var conversationOptions = Options.Create(new ConversationOptions
        {
            MaxToolRounds = maxToolRounds,
            ReasoningEffort = "low",
            EnableSensitiveData = enableSensitiveData,
        });
        var anthropicOptions = Options.Create(new AnthropicOptions { ApiKey = apiKey });

        _searchLog = MemeSearchTestServices.NewLogWriter(fixture.ConnectionString);
        var registry = new ConversationToolRegistry(
            new MemeSearchService(NewContext(), _searchLog),
            new GuildStatsService(NewContext()),
            new DatabaseQueryService(NewContext(), conversationOptions, NullLogger<DatabaseQueryService>.Instance),
            new FakeGuildLiveStateService(),
            new FakeGuildActionService(),
            new FakeConfirmationService(),
            new DatabaseSchemaHint("schema hint"),
            conversationOptions,
            NullLogger<ConversationToolRegistry>.Instance);

        return new ConversationService(
            client,
            registry,
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            anthropicOptions,
            new TestHostEnvironment(),
            logger ?? NullLogger<ConversationService>.Instance);
    }

    private static async Task<List<ConversationUpdate>> CollectAsync(IAsyncEnumerable<ConversationUpdate> stream)
    {
        List<ConversationUpdate> events = [];
        await foreach (var update in stream)
            events.Add(update);
        return events;
    }

    private static string FirstDelta(IEnumerable<ConversationUpdate> events) =>
        events.OfType<ConversationUpdate.AssistantTextDelta>().First().Text;

    private static int IndexOf<TUpdate>(IReadOnlyList<ConversationUpdate> events) where TUpdate : ConversationUpdate
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is TUpdate)
                return i;
        }

        return -1;
    }

    private static int IndexOfContent<TContent>(IReadOnlyList<ChatMessage> messages) where TContent : AIContent
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Contents.OfType<TContent>().Any())
                return i;
        }

        return -1;
    }

    private static ChatResponseUpdate ToolCallUpdate(string callId, string name, Dictionary<string, object?> arguments) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);

    private static IReadOnlyList<ChatResponseUpdate> StreamToolCall(
        string callId, string name, Dictionary<string, object?> arguments) =>
        [ToolCallUpdate(callId, name, arguments)];

    private static IReadOnlyList<ChatResponseUpdate> StreamText(params string[] deltas) =>
        deltas.Select(delta => new ChatResponseUpdate(ChatRole.Assistant, delta)).ToList();

    private async Task SeedTurtleMemeAsync()
    {
        var message = new MessageEntity
        {
            DiscordId = MemeMessageId,
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = message.Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = MemeMessageId,
            AttachmentDiscordId = 41UL,
            FileName = "meme-41.png",
            FileSizeBytes = 1234,
            ContentType = "image/png",
            ContentHash = "hash-41",
            Status = MemeIndexStatus.Indexed,
            Annotations =
            [
                new MemeAnnotationEntity
                {
                    AttachmentDiscordId = 41UL,
                    ModelId = "google/gemini-3-flash-preview",
                    PromptVersion = OpenRouterClient.PromptVersion,
                    IndexedAtUtc = DateTime.UtcNow,
                    DescriptionPl = "Unikatowy żółw na deskorolce",
                    DescriptionEn = "A unique turtle on a skateboard",
                    OcrText = "",
                    Tags = ["żółw"],
                    RawResponseJson = "{}",
                },
            ],
        });
        await _db.SaveChangesAsync();
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    // A fake IChatClient that returns scripted streaming responses and records each call's
    // transcript and whether tools were offered.
    private sealed class ScriptedChatClient(
        Func<int, IReadOnlyList<ChatMessage>, ChatOptions?, IReadOnlyList<ChatResponseUpdate>> responder) : IChatClient
    {
        private int _callIndex;

        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];
        public List<bool> CallHadTools { get; } = [];
        public List<bool> CallForbadeTools { get; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var snapshot = messages.ToList();
            Calls.Add(snapshot);
            CallHadTools.Add(options?.Tools is { Count: > 0 });
            CallForbadeTools.Add(options?.ToolMode is NoneChatToolMode);

            foreach (var update in responder(_callIndex++, snapshot, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
                await Task.Yield();
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("§3 drives the loop via GetStreamingResponseAsync.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
