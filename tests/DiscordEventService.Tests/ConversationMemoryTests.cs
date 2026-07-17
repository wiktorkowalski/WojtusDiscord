using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using Xunit;

namespace DiscordEventService.Tests;

// #267 conversation memory + usage ledger, against a real Postgres (Testcontainers, no
// DB mocking). The headline test is the byte-fidelity replay (research A-OQ1): a stored
// tool round must rehydrate into the exact wire bytes the model saw live — through the
// REAL MEAI OpenAI adapter with the HTTP transport faked, so the `object?` Result trap
// (a JsonElement replays quote-wrapped) would be caught here, not in production.
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
        var transport = new CaptureTransport(callIndex => callIndex switch
        {
            // Turn 1, round 1: the model calls meme_search (unicode args stress the bytes).
            0 => SseToolCall("call_1", "meme_search", "{\"query\":\"żółw\",\"limit\":5}"),
            // Turn 1, round 2: final answer + usage frame (exercises the ledger extraction).
            1 => SseText("Znalazłem mema o żółwiu."),
            // Turn 2 (after "restart"): plain answer.
            _ => SseText("Tak, to ten sam żółw."),
        });

        // Turn 1 — live: real adapter, real tool dispatch against the seeded meme.
        await CollectAsync(BuildRealAdapterService(transport, out _)
            .GenerateReplyAsync("znajdź mema o żółwiu", Context(), CancellationToken.None));

        // Turn 2 — fresh service + fresh DbContexts = a process restart; same channel.
        await CollectAsync(BuildRealAdapterService(transport, out _)
            .GenerateReplyAsync("a to na pewno żółw?", Context(), CancellationToken.None));

        Assert.Equal(3, transport.RequestBodies.Count);

        // Live wire: turn 1's second call carried the tool round back to the model.
        var liveToolMessage = FindMessage(transport.RequestBodies[1], "tool");
        var liveToolCalls = FindToolCallsArray(transport.RequestBodies[1]);
        // Replayed wire: turn 2's only call rehydrated that round from the store.
        var replayedToolMessage = FindMessage(transport.RequestBodies[2], "tool");
        var replayedToolCalls = FindToolCallsArray(transport.RequestBodies[2]);

        // THE assertion: byte-identical wire replay (a JsonElement-typed Result would
        // come back quote-wrapped/escaped here and fail).
        Assert.Equal(liveToolMessage, replayedToolMessage);
        Assert.Equal(liveToolCalls, replayedToolCalls);

        // And the replayed transcript actually carried turn 1 (user + final answer).
        var turn2Messages = MessagesOf(transport.RequestBodies[2]);
        Assert.Contains(turn2Messages, m => m.Contains("znajdź mema o żółwiu"));
        Assert.Contains(turn2Messages, m => m.Contains("Znalazłem mema o żółwiu."));
    }

    [Fact]
    public async Task ToolRound_LedgerRowsPerRound_WithCostTokensAndInvoker()
    {
        var transport = new CaptureTransport(callIndex => callIndex switch
        {
            0 => SseToolCall("call_1", "meme_search", "{\"query\":\"żółw\",\"limit\":5}"),
            _ => SseText("Gotowe."),
        });

        await CollectAsync(BuildRealAdapterService(transport, out var conversationOptions)
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
        });
        Assert.Equal([1, 2], rows.Select(r => r.Round));
        // The canned usage frame flows through the raw ChatTokenUsage patch into the row.
        Assert.All(rows, row =>
        {
            Assert.Equal(10, row.PromptTokens);
            Assert.Equal(5, row.CompletionTokens);
            Assert.Equal(0.01, row.CostUsd);
            Assert.Equal(0.008, row.UpstreamInferenceCostUsd);
        });

        // "Cost per user this month" is a single WHERE (#256).
        var monthlyCost = await _db.ConversationUsage
            .Where(u => u.InvokerId == InvokerId && u.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30))
            .SumAsync(u => u.CostUsd ?? 0);
        Assert.Equal(0.02, monthlyCost, precision: 10);
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

    // The full production stack minus the network: real MEAI OpenAI adapter over a
    // scripted SSE transport, real tool registry, real store.
    private ConversationService BuildRealAdapterService(
        CaptureTransport transport, out IOptions<ConversationOptions> conversationOptions)
    {
        conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            PrimaryGuildId = GuildDiscordId,
            InterimNarration = "-# interim",
        });
        var openRouterOptions = Options.Create(new OpenRouterOptions { ApiKey = "test-key" });

        var chatClient = new OpenAIClient(
                new ApiKeyCredential("test-key"),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("http://localhost/api/v1"),
                    Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
                })
            .GetChatClient(conversationOptions.Value.Model)
            .AsIChatClient();

        return new ConversationService(
            chatClient,
            BuildRegistry(conversationOptions),
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            openRouterOptions,
            NullLogger<ConversationService>.Instance);
    }

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
            Options.Create(new OpenRouterOptions { ApiKey = "test-key" }),
            NullLogger<ConversationService>.Instance);
    }

    private ConversationToolRegistry BuildRegistry(IOptions<ConversationOptions> conversationOptions) =>
        new(new MemeSearchService(NewContext()),
            new GuildStatsService(NewContext()),
            new DatabaseQueryService(NewContext(), conversationOptions, NullLogger<DatabaseQueryService>.Instance),
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

    private static List<string> MessagesOf(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Select(m => m.GetRawText())
            .ToList();
    }

    private static string FindMessage(string requestBody, string role)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var match = doc.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Single(m => m.GetProperty("role").GetString() == role);
        return match.GetRawText();
    }

    private static string FindToolCallsArray(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var match = doc.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Single(m => m.TryGetProperty("tool_calls", out _));
        return match.GetProperty("tool_calls").GetRawText();
    }

    private const string UsageJson =
        "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15," +
        "\"cost\":0.01,\"cost_details\":{\"upstream_inference_cost\":0.008}}";

    private static string SseChunk(string choicesJson, string? extraJson = null) =>
        "data: {\"id\":\"chatcmpl-t\",\"object\":\"chat.completion.chunk\",\"created\":1750000000," +
        $"\"model\":\"anthropic/claude-sonnet-4.6\",\"choices\":{choicesJson}{(extraJson is null ? "" : "," + extraJson)}}}\n\n";

    private static string SseText(string text)
    {
        var delta = JsonSerializer.Serialize(text);
        return SseChunk($"[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"content\":{delta}}},\"finish_reason\":null}}]")
            + SseChunk("[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]", UsageJson)
            + "data: [DONE]\n\n";
    }

    private static string SseToolCall(string callId, string toolName, string argumentsJson)
    {
        var escapedArgs = JsonSerializer.Serialize(argumentsJson);
        return SseChunk(
                $"[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"tool_calls\":[{{\"index\":0,\"id\":\"{callId}\"," +
                $"\"type\":\"function\",\"function\":{{\"name\":\"{toolName}\",\"arguments\":{escapedArgs}}}}}]}},\"finish_reason\":null}}]")
            + SseChunk("[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]", UsageJson)
            + "data: [DONE]\n\n";
    }

    // Captures every request body and answers with the scripted SSE stream — the fake
    // seam sits UNDER the real OpenAI SDK + MEAI adapter, which is the point: the wire
    // bytes asserted on are the ones the adapter actually produces.
    private sealed class CaptureTransport(Func<int, string> sseResponder) : HttpMessageHandler
    {
        private int _callIndex;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseResponder(_callIndex++), Encoding.UTF8, "text/event-stream"),
            };
            return response;
        }
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
            DescriptionPl = "Żółw na deskorolce",
            DescriptionEn = "A turtle on a skateboard",
            OcrText = "",
            Tags = ["żółw"],
            ModelId = "google/gemini-3-flash-preview",
            RawResponseJson = "{}",
            IndexedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
