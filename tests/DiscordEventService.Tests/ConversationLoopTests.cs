using System.Diagnostics;
using System.Runtime.CompilerServices;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
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
// stream, a tool-batch summary closing each tool round (with no injected canned cue —
// the model's own narration is the progress note, #274), the tool result fed back into
// the next model call, a final answer accumulated from deltas, and the round cap
// terminating the loop with tools withheld.
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
        // cue (#274) — the tool-batch summary is the round's only render event.
        var summary = Assert.Single(events.OfType<ConversationUpdate.ToolBatchSummary>());
        Assert.Contains("meme_search", summary.Text);
        Assert.True(IndexOf<ConversationUpdate.AssistantTextDelta>(events) > IndexOf<ConversationUpdate.ToolBatchSummary>(events),
            "a silent tool round should yield no text before its summary");
        // The answer is the deltas after the summary, accumulated into one message.
        Assert.Equal(finalAnswer, FinalAnswer(events));

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

        // The model's own narration is surfaced and precedes the tool-batch summary.
        Assert.Equal("Sprawdzam memy. ", FirstDelta(events));
        Assert.True(IndexOf<ConversationUpdate.AssistantTextDelta>(events) < IndexOf<ConversationUpdate.ToolBatchSummary>(events));
        Assert.Single(events.OfType<ConversationUpdate.ToolBatchSummary>());
        // The answer is accumulated from its deltas.
        Assert.Equal("Oto co znalazłem.", FinalAnswer(events));
    }

    [Fact]
    public async Task GenerateReplyAsync_ModelNeverStops_HitsRoundCapAndFinalizesWithoutTools()
    {
        const int cap = 3;
        const string cappedAnswer = "Okay, stopping here.";
        // Always ask for a tool while tools are offered; answer once they're withheld.
        var client = new ScriptedChatClient((callIndex, _, options) =>
            options?.Tools is { Count: > 0 }
                ? StreamToolCall($"call_{callIndex}", "meme_search",
                    new Dictionary<string, object?> { ["query"] = "x", ["limit"] = 1 })
                : StreamText(cappedAnswer));

        var service = BuildService(client, maxToolRounds: cap);
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        var events = await CollectAsync(service.GenerateReplyAsync("loop forever", context, CancellationToken.None));

        Assert.Equal(cappedAnswer, FinalAnswer(events));
        // cap rounds offering tools, then one finalizing call with tools withheld.
        Assert.Equal(cap + 1, client.Calls.Count);
        Assert.Equal([true, true, true, false], client.CallHadTools);
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
        const string question = "znajdz mema o zolwiu";
        const string answer = "Found the turtle meme for you.";
        var log = new RecordingLogger();
        var service = BuildService(
            ScriptToolThenAnswer(answer), enableSensitiveData: true, logger: log.For<ConversationService>());
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId: 7UL);

        await CollectAsync(service.GenerateReplyAsync(question, context, CancellationToken.None));

        var line = Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message;
        Assert.EndsWith($"question {question}, answer {answer}", line);
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
        ILogger<ConversationService>? logger = null)
    {
        var conversationOptions = Options.Create(new ConversationOptions
        {
            MaxToolRounds = maxToolRounds,
            ReasoningEffort = "low",
            EnableSensitiveData = enableSensitiveData,
        });
        var openRouterOptions = Options.Create(new OpenRouterOptions { ApiKey = "test-key" });

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
            openRouterOptions,
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

    // The deltas after the last tool-batch summary form the final answer message.
    private static string FinalAnswer(IReadOnlyList<ConversationUpdate> events)
    {
        var lastSummary = -1;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is ConversationUpdate.ToolBatchSummary)
                lastSummary = i;
        }

        return string.Concat(events.Skip(lastSummary + 1)
            .OfType<ConversationUpdate.AssistantTextDelta>()
            .Select(delta => delta.Text));
    }

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

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var snapshot = messages.ToList();
            Calls.Add(snapshot);
            CallHadTools.Add(options?.Tools is { Count: > 0 });

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
