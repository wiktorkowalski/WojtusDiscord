using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// #267 conversation memory + usage ledger, against a real Postgres (Testcontainers, no
// DB mocking). The headline test is the byte-fidelity replay (research A-OQ1): a stored
// tool round must rehydrate into the exact wire bytes the model saw live — through the
// REAL AnthropicClient + MEAI adapter with the HTTP transport faked (#426), so the `object?`
// Result trap (a JsonElement replays quote-wrapped) and a split of parallel tool results
// into separate user messages would be caught here, not in production.
public sealed class ConversationMemoryTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 21UL;
    private const ulong ChannelDiscordId = 22UL;
    private const ulong ThreadChannelId = 23UL;
    private const ulong InvokerId = 42UL;
    private const ulong MemeMessageId = 2101UL;

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.ConversationMessages.ExecuteDeleteAsync();
        await _db.ConversationUsage.ExecuteDeleteAsync();
        await _db.Conversations.ExecuteDeleteAsync();
        await _db.MemeAnnotations.ExecuteDeleteAsync();
        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        await SeedTurtleMemeAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // Byte-fidelity (the A-OQ1 test): live tool round -> store -> restart -> follow-up.
    // The tool message and the assistant tool_calls the model sees on the follow-up must
    // be byte-identical to what it saw live in turn 1.
    [Fact]
    public async Task FollowUpAfterRestart_ReplaysToolRound_ByteIdenticalOnTheWire()
    {
        var handler = ScriptedClaudeHandler.Streams(callIndex => callIndex switch
        {
            // Turn 1, round 1: the model calls two tools at once (unicode args stress the
            // bytes; a JsonElement result and a plain string result).
            0 => ClaudeSse.Start()
                .Thinking()
                .ToolUse("toolu_1", "meme_search", "{\"query\":\"żółw\",\"limit\":5}")
                .ToolUse("toolu_2", "server_info", "{}")
                .End("tool_use"),
            // Turn 1, round 2: final answer.
            1 => ClaudeSse.TextAnswer("Znalazłem mema o żółwiu."),
            // Turn 2 (after "restart"): plain answer.
            _ => ClaudeSse.TextAnswer("Tak, to ten sam żółw."),
        });

        // Turn 1 — live: real adapter, real tool dispatch against the seeded meme.
        await CollectAsync(BuildRealAdapterService(handler, out _)
            .GenerateReplyAsync("znajdź mema o żółwiu", Context(), CancellationToken.None));

        // Turn 2 — fresh service + fresh DbContexts = a process restart; same channel.
        await CollectAsync(BuildRealAdapterService(handler, out _)
            .GenerateReplyAsync("a to na pewno żółw?", Context(), CancellationToken.None));

        Assert.Equal(3, handler.CallCount);
        var live = handler.Requests[1];
        var replayed = handler.Requests[2];

        // THE assertion: byte-identical wire replay of the tool round (a JsonElement-typed
        // Result would come back quote-wrapped/escaped here and fail). Both results ride ONE
        // user message, live and replayed. Only the cache breakpoint may differ: live, the
        // results are the last message.
        Assert.Equal(
            ClaudeRequest.WithoutCacheControl(ToolResultMessage(live)),
            ClaudeRequest.WithoutCacheControl(ToolResultMessage(replayed)));
        Assert.Equal(["tool_result", "tool_result"], ClaudeRequest.BlockTypes(ToolResultMessage(replayed)));
        Assert.Equal(ToolUseBlocks(live), ToolUseBlocks(replayed));

        // And the replayed transcript actually carried turn 1 (user + final answer).
        var turn2Messages = ClaudeRequest.Messages(replayed)
            .SelectMany(m => m!["content"]!.AsArray())
            .Select(block => block!["text"]?.GetValue<string>() ?? "")
            .ToList();
        Assert.Contains(turn2Messages, m => m.Contains("znajdź mema o żółwiu"));
        Assert.Contains(turn2Messages, m => m.Contains("Znalazłem mema o żółwiu."));
    }

    [Fact]
    public async Task ToolRound_LedgerRowsPerRound_WithCostTokensAndInvoker()
    {
        var handler = ScriptedClaudeHandler.Streams(callIndex => callIndex switch
        {
            0 => ClaudeSse.Start(input: 100, cacheWrite: 2000)
                .ToolUse("toolu_1", "meme_search", "{\"query\":\"żółw\",\"limit\":5}")
                .End("tool_use", output: 50),
            _ => ClaudeSse.Start(input: 100, cacheRead: 2000).Text("Gotowe.").End(output: 50),
        });

        await CollectAsync(BuildRealAdapterService(handler, out var conversationOptions)
            .GenerateReplyAsync("znajdź mema o żółwiu", Context(), CancellationToken.None));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Round).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(InvokerId, row.InvokerId);
            Assert.Equal(conversationOptions.Value.Model, row.Model);
            Assert.Equal(1, row.Attempt);
            Assert.False(row.Failed);
            Assert.Equal(0, row.TurnIndex);
            Assert.True(row.LatencyMs >= 0);
            Assert.Equal(2100, row.PromptTokens);
            Assert.Equal(50, row.CompletionTokens);
            // No search: the whole cost is token cost.
            Assert.Equal(row.CostUsd, row.UpstreamInferenceCostUsd);
        });
        Assert.Equal([1, 2], rows.Select(r => r.Round));
        Assert.Equal((0, 2000), (rows[0].CacheReadTokens, rows[0].CacheWriteTokens));
        Assert.Equal((2000, 0), (rows[1].CacheReadTokens, rows[1].CacheWriteTokens));
        // 100 × $2 + 2000 × $2.50 + 50 × $10 per MTok; then 100 × $2 + 2000 × $0.20 + 50 × $10.
        Assert.Equal(0.0057, rows[0].CostUsd!.Value, precision: 9);
        Assert.Equal(0.0011, rows[1].CostUsd!.Value, precision: 9);

        // The prompt tokens also land on the round's first assistant transcript row.
        Assert.Contains(await _db.ConversationMessages.ToListAsync(), m => m.PromptTokens == 2100);

        // "Cost per user this month" is a single WHERE (#256).
        var monthlyCost = await _db.ConversationUsage
            .Where(u => u.InvokerId == InvokerId && u.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30))
            .SumAsync(u => u.CostUsd ?? 0);
        Assert.Equal(0.0068, monthlyCost, precision: 9);
    }

    [Fact]
    public async Task FollowUpTurn_SeesPriorUserAndAssistantMessages()
    {
        var client = new RecordingChatClient(_ => Stream("Pierwsza odpowiedź."));
        var service = BuildScriptedService(client);
        await CollectAsync(service.GenerateReplyAsync("pierwsze pytanie", Context(), CancellationToken.None));

        var followUpClient = new RecordingChatClient(_ => Stream("Druga odpowiedź."));
        await CollectAsync(BuildScriptedService(followUpClient)
            .GenerateReplyAsync("drugie pytanie", Context(), CancellationToken.None));

        var transcript = Assert.Single(followUpClient.Calls);
        Assert.Equal(ChatRole.System, transcript[0].Role);
        Assert.Equal(ChatRole.User, transcript[^1].Role);
        Assert.Equal("drugie pytanie", transcript[^1].Text);
        Assert.Contains(transcript, m => m.Role == ChatRole.User && m.Text == "pierwsze pytanie");
        Assert.Contains(transcript, m => m.Role == ChatRole.Assistant && m.Text == "Pierwsza odpowiedź.");
    }

    [Fact]
    public async Task Window_TokenBudget_DropsOldestMessagesFirst()
    {
        var memory = BuildMemory(windowTokenBudget: 30);
        var turn = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);
        // ~25 est tokens each (100 chars / 4) — only one fits a 30-token budget.
        await memory.PersistUserMessageAsync(turn with { TurnIndex = 0 }, new string('a', 100), CancellationToken.None);
        await memory.PersistUserMessageAsync(turn with { TurnIndex = 1 }, new string('b', 100), CancellationToken.None);

        var next = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        var replayed = Assert.Single(next.Window);
        Assert.Equal(new string('b', 100), replayed.Text);
        Assert.Equal(2, next.TurnIndex);
    }

    [Fact]
    public async Task Window_NeverSplitsToolCallGroup_DropsItWhole()
    {
        var memory = BuildMemory(windowTokenBudget: 40);
        var turn = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        // One tool-call group (~50+ est tokens: assistant row + huge tool result).
        var assistantWithCall = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "meme_search", new Dictionary<string, object?> { ["query"] = "x" })]);
        await memory.PersistAssistantMessagesAsync(turn, [assistantWithCall], null, null, CancellationToken.None);
        await memory.PersistToolResultAsync(turn, "meme_search",
            new FunctionResultContent("call_1", new string('r', 200)), CancellationToken.None);
        // A small later answer that fits on its own.
        await memory.PersistAssistantMessagesAsync(turn,
            [new ChatMessage(ChatRole.Assistant, "krótka odpowiedź")], null, null, CancellationToken.None);

        var next = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        // The over-budget group was dropped WHOLE — no dangling FunctionCallContent or
        // FunctionResultContent may survive (a dangling tool_call_id is a provider 400).
        var replayed = Assert.Single(next.Window);
        Assert.Equal("krótka odpowiedź", replayed.Text);
        Assert.DoesNotContain(next.Window, m => m.Contents.OfType<FunctionCallContent>().Any());
        Assert.DoesNotContain(next.Window, m => m.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task Window_IncompleteToolGroup_CrashBeforeToolResult_IsNotReplayed()
    {
        var memory = BuildMemory();
        var turn = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        await memory.PersistUserMessageAsync(turn, "pytanie", CancellationToken.None);
        // Crash simulation: the assistant's tool call was persisted, its result never was.
        var assistantWithCall = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_1", "meme_search", new Dictionary<string, object?> { ["query"] = "x" })]);
        await memory.PersistAssistantMessagesAsync(turn, [assistantWithCall], null, null, CancellationToken.None);

        var next = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        var replayed = Assert.Single(next.Window);
        Assert.Equal("pytanie", replayed.Text);
    }

    [Fact]
    public async Task Reasoning_IsPersistedButStrippedFromReplay()
    {
        var memory = BuildMemory();
        var turn = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        var withReasoning = new ChatMessage(ChatRole.Assistant,
            [new TextReasoningContent("tajne rozumowanie"), new TextContent("odpowiedź")]);
        await memory.PersistAssistantMessagesAsync(turn, [withReasoning], null, null, CancellationToken.None);

        var row = await _db.ConversationMessages.SingleAsync();
        Assert.Equal("tajne rozumowanie", row.Reasoning);

        var next = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);
        var replayed = Assert.Single(next.Window);
        Assert.Equal("odpowiedź", replayed.Text);
        Assert.DoesNotContain(replayed.Contents, c => c is TextReasoningContent);
    }

    [Fact]
    public async Task ReasoningOnlyAssistantRow_IsNotReplayedAsAnEmptyMessage()
    {
        var memory = BuildMemory();
        var turn = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        await memory.PersistUserMessageAsync(turn, "pytanie", CancellationToken.None);
        // Degenerate round: reasoning but no visible text and no tool call.
        await memory.PersistAssistantMessagesAsync(turn,
            [new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("tylko rozumowanie")])],
            null, null, CancellationToken.None);

        var next = await memory.BeginTurnAsync(ThreadChannelId, GuildDiscordId, CancellationToken.None);

        // The row is persisted for the record but must not replay as an empty message.
        Assert.Equal(2, await _db.ConversationMessages.CountAsync());
        var replayed = Assert.Single(next.Window);
        Assert.Equal("pytanie", replayed.Text);
    }

    private static ConversationContext Context() =>
        new(GuildDiscordId, InvokerId, "tester", IsAdmin: false, ChannelId: ThreadChannelId);

    private ConversationMemoryService BuildMemory(int windowTokenBudget = 12000, int windowMaxMessages = 40)
    {
        var options = Options.Create(new ConversationOptions
        {
            WindowTokenBudget = windowTokenBudget,
            WindowMaxMessages = windowMaxMessages,
        });
        return new ConversationMemoryService(NewContext(), options, NullLogger<ConversationMemoryService>.Instance);
    }

    // The full production stack minus the network: real AnthropicClient + MEAI adapter over a
    // scripted SSE transport, real tool registry, real store.
    private ConversationService BuildRealAdapterService(
        ScriptedClaudeHandler handler, out IOptions<ConversationOptions> conversationOptions)
    {
        conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            PrimaryGuildId = GuildDiscordId,
        });

        return new ConversationService(
            handler.CreateChatClient(conversationOptions.Value),
            BuildRegistry(conversationOptions),
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            new TestHostEnvironment(),
            NullLogger<ConversationService>.Instance);
    }

    // The user message holding the tool_result blocks.
    private static JsonNode ToolResultMessage(JsonObject request) =>
        ClaudeRequest.Messages(request).Single(m => ClaudeRequest.BlockTypes(m!).Contains("tool_result"))!;

    private static string ToolUseBlocks(JsonObject request) =>
        new JsonArray([.. ClaudeRequest.Messages(request)
            .SelectMany(m => m!["content"]!.AsArray())
            .Where(b => b!["type"]!.GetValue<string>() == "tool_use")
            .Select(b => b!.DeepClone())]).ToJsonString();

    private ConversationService BuildScriptedService(IChatClient client)
    {
        var conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            PrimaryGuildId = GuildDiscordId,
        });
        return new ConversationService(
            client,
            BuildRegistry(conversationOptions),
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            new TestHostEnvironment(),
            NullLogger<ConversationService>.Instance);
    }

    private ConversationToolRegistry BuildRegistry(IOptions<ConversationOptions> conversationOptions) =>
        new(MemeSearchTestServices.NewSearch(NewContext(), fixture.ConnectionString),
            new GuildStatsService(NewContext()),
            new DatabaseQueryService(NewContext(), conversationOptions, NullLogger<DatabaseQueryService>.Instance),
            new FakeGuildLiveStateService(),
            new FakeGuildActionService(),
            new FakeConfirmationService(),
            new DatabaseSchemaHint("schema hint"),
            conversationOptions,
            NullLogger<ConversationToolRegistry>.Instance);

    private static async Task<List<ConversationUpdate>> CollectAsync(IAsyncEnumerable<ConversationUpdate> stream)
    {
        List<ConversationUpdate> events = [];
        await foreach (var update in stream)
            events.Add(update);
        return events;
    }

    // A fake IChatClient recording each call's transcript (cheap-path recall tests).
    private sealed class RecordingChatClient(
        Func<int, IReadOnlyList<ChatResponseUpdate>> responder) : IChatClient
    {
        private int _callIndex;

        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            foreach (var update in responder(_callIndex++))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
                await Task.Yield();
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the loop streams");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static IReadOnlyList<ChatResponseUpdate> Stream(params string[] deltas) =>
        deltas.Select(delta => new ChatResponseUpdate(ChatRole.Assistant, delta)).ToList();

    private async Task SeedTurtleMemeAsync()
    {
        var guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();

        var channel = new ChannelEntity
        {
            DiscordId = ChannelDiscordId,
            GuildId = guild.Id,
            Name = "memes",
            Type = ChannelType.Text
        };
        var author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(channel);
        _db.Users.Add(author);
        await _db.SaveChangesAsync();

        var message = new MessageEntity
        {
            DiscordId = MemeMessageId,
            ChannelId = channel.Id,
            GuildId = guild.Id,
            AuthorId = author.Id,
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
            AttachmentDiscordId = 61UL,
            FileName = "zolw.png",
            FileSizeBytes = 1234,
            ContentType = "image/png",
            ContentHash = "hash-61",
            Status = MemeIndexStatus.Indexed,
            Annotations =
            [
                new MemeAnnotationEntity
                {
                    AttachmentDiscordId = 61UL,
                    ModelId = "google/gemini-3-flash-preview",
                    PromptVersion = OpenRouterClient.PromptVersion,
                    IndexedAtUtc = DateTime.UtcNow,
                    DescriptionPl = "Żółw na deskorolce",
                    DescriptionEn = "A turtle on a skateboard",
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
}
