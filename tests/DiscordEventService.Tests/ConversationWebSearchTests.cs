using System.Diagnostics;
using System.Text;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The native web search server tool end-to-end (#271, #426): a scripted SSE transport UNDER
// the real AnthropicClient + MEAI adapter proves the wire body (the turn's strict function
// tools, then `web_search_20260209`), the round trip of the server-tool blocks into the next
// round (the adapter alone drops them), pause_turn continuation, citation recovery from
// citations_delta, and the ledger itemisation of the search fee.
public sealed class ConversationWebSearchTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 51UL;
    private const ulong ThreadChannelId = 53UL;
    private const ulong InvokerId = 54UL;

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.ConversationMessages.ExecuteDeleteAsync();
        await _db.ConversationUsage.ExecuteDeleteAsync();
        await _db.Conversations.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RequestBody_StrictSortedAppTools_ThenWebSearchLast()
    {
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("Cześć."));

        await CollectAsync(BuildService(handler, webSearchEnabled: true)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        var tools = Assert.Single(handler.Requests)["tools"]!.AsArray();
        var functions = tools.Take(tools.Count - 1).ToList();
        Assert.NotEmpty(functions);
        // Every app tool has the strict schema, so every one is sent with strict: true.
        Assert.All(functions, tool => Assert.True(tool!["strict"]!.GetValue<bool>()));
        var names = functions.Select(tool => tool!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal("web_search_20260209", tools[^1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task RequestBody_WebSearchDisabled_NoServerToolOnTheWire()
    {
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("Cześć."));

        await CollectAsync(BuildService(handler, webSearchEnabled: false)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        var tools = Assert.Single(handler.Requests)["tools"]!.AsArray();
        Assert.DoesNotContain(tools, tool => tool!["name"]!.GetValue<string>() == "web_search");
    }

    [Fact]
    public async Task MixedTurn_SearchBlocksAndThinkingRoundTrip_CitationsDeduped_FeeLedgered()
    {
        // Round 1 is the mixed shape: thinking, narration with a citation, a web search, AND an
        // app tool call. Round 2 answers in two text blocks with a second citation plus a
        // duplicate of the first URL (a later round may re-search) — the source list dedupes it.
        var handler = ScriptedClaudeHandler.Streams(call => call switch
        {
            0 => ClaudeSse.Start(input: 1000, cacheWrite: 4000)
                .Thinking("muszę poszukać")
                .Text("Sprawdzam w sieci. ", ("https://example.com/article", "Article"))
                .WebSearch("srvtoolu_1", "żółw news", ("https://example.com/article", "Article"))
                .ToolUse("toolu_1", "meme_search", """{"query":"zolw","limit":5}""")
                .End("tool_use", output: 200, webSearches: 1),
            _ => ClaudeSse.Start(input: 300, cacheRead: 5000)
                .Text("Oto co ", ("https://news.example.org/story", "Story"))
                .Text("znalazłem.", ("https://example.com/article", "Article"))
                .End(output: 100),
        });

        using var metrics = new MetricsCapture();
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConversationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (spans)
                    spans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        var events = await CollectAsync(BuildService(handler, webSearchEnabled: true)
            .GenerateReplyAsync("co się dzieje w świecie?", Context(), CancellationToken.None));

        // The cache write lands on the generation span of round 1, not on the turn span.
        List<Activity> stopped;
        lock (spans)
            stopped = [.. spans];
        var turnSpan = stopped.Single(s => s.OperationName == "conversation.turn"
            && s.GetTagItem("conversation.invoker_id") is ulong id && id == InvokerId);
        var tagged = Assert.Single(stopped, s => s.TraceId == turnSpan.TraceId
            && s.GetTagItem(CacheUsageTagChatClient.CacheCreationTag) is not null);
        Assert.StartsWith("chat", tagged.OperationName);
        Assert.Equal(4000L, tagged.GetTagItem(CacheUsageTagChatClient.CacheCreationTag));

        var answer = FinalAnswer(events);
        Assert.StartsWith("Oto co znalazłem.", answer);
        Assert.EndsWith("-# 🔗 [example.com](https://example.com/article) · " +
            "[news.example.org](https://news.example.org/story)", answer);

        // Round 2 carries round 1's assistant message as the model wrote it: one thinking block
        // with its signature, the text, the search call (query rebuilt) and its result (raw,
        // encrypted_content intact), then the tool call — and all tool results in one message.
        var round2 = handler.Requests[1];
        var messages = ClaudeRequest.Messages(round2);
        var assistant = messages[^2]!;
        Assert.Equal("assistant", assistant["role"]!.GetValue<string>());
        Assert.Equal(["thinking", "text", "server_tool_use", "web_search_tool_result", "tool_use"],
            ClaudeRequest.BlockTypes(assistant));
        var blocks = assistant["content"]!.AsArray();
        Assert.Equal("muszę poszukać", blocks[0]!["thinking"]!.GetValue<string>());
        Assert.Equal(ClaudeSse.Signature, blocks[0]!["signature"]!.GetValue<string>());
        Assert.Equal("srvtoolu_1", blocks[2]!["id"]!.GetValue<string>());
        Assert.Equal("web_search", blocks[2]!["name"]!.GetValue<string>());
        Assert.Equal("żółw news", blocks[2]!["input"]!["query"]!.GetValue<string>());
        Assert.Equal("srvtoolu_1", blocks[3]!["tool_use_id"]!.GetValue<string>());
        Assert.Equal("ENC_Article", blocks[3]!["content"]![0]!["encrypted_content"]!.GetValue<string>());
        Assert.Equal(["tool_result"], ClaudeRequest.BlockTypes(messages[^1]!));
        Assert.InRange(ClaudeRequest.CountCacheBreakpoints(round2), 0, AnthropicChatOptions.MaxCacheBreakpoints);

        // The ledger: tokens with the cache split, the computed cost, and the token-only cost
        // in upstream_inference_cost_usd (cost − upstream = the search fee).
        var rows = await _db.ConversationUsage.OrderBy(u => u.Round).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal((5000, 0, 4000, 200, 1), (rows[0].PromptTokens!.Value, rows[0].CacheReadTokens!.Value,
            rows[0].CacheWriteTokens!.Value, rows[0].CompletionTokens!.Value, rows[0].WebSearchRequests!.Value));
        // 1000 × $2 + 4000 × $2.50 + 200 × $10 per MTok, plus one search at $0.01.
        Assert.Equal(0.014, rows[0].UpstreamInferenceCostUsd!.Value, precision: 9);
        Assert.Equal(0.024, rows[0].CostUsd!.Value, precision: 9);
        Assert.Equal((5300, 5000, 0, 100), (rows[1].PromptTokens!.Value, rows[1].CacheReadTokens!.Value,
            rows[1].CacheWriteTokens!.Value, rows[1].CompletionTokens!.Value));
        Assert.Null(rows[1].WebSearchRequests);
        // 300 × $2 + 5000 × $0.20 + 100 × $10 per MTok.
        Assert.Equal(0.0026, rows[1].CostUsd!.Value, precision: 9);

        // The search fee metric: cost less the token cost. Other test classes use the same
        // model label, so the assertion is on this turn's own value.
        var fees = metrics.Of("wojtus.conversation.web_search.cost.usd").Select(m => Math.Round(m.Value, 6)).ToList();
        Assert.Contains(0.01, fees);
        Assert.True(metrics.Of("wojtus.conversation.context.messages").Count >= 2);
        Assert.All(metrics.Of("wojtus.conversation.first_token"), m => Assert.True(m.Tags.ContainsKey("model")));
    }

    // A long search turn the API paused: the response goes back unchanged (with its search
    // blocks), the model goes on, and the two halves form one answer.
    [Fact]
    public async Task PauseTurn_SendsThePausedTurnBack_AndContinues()
    {
        var handler = ScriptedClaudeHandler.Streams(call => call switch
        {
            0 => ClaudeSse.Start()
                .Text("Szukam… ")
                .WebSearch("srvtoolu_9", "pogoda kraków", ("https://weather.example/krk", "Weather"))
                .End("pause_turn", webSearches: 1),
            _ => ClaudeSse.Start().Text("Słonecznie.", ("https://weather.example/krk", "Weather")).End(),
        });

        var events = await CollectAsync(BuildService(handler, webSearchEnabled: true)
            .GenerateReplyAsync("jaka pogoda?", Context(), CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
        Assert.DoesNotContain(events, e => e is ConversationUpdate.ToolBatchSummary);
        Assert.Equal("Szukam… Słonecznie.\n-# 🔗 [weather.example](https://weather.example/krk)", FinalAnswer(events));

        // The second request ends with the paused assistant message, search blocks included.
        var last = ClaudeRequest.Messages(handler.Requests[1])[^1]!;
        Assert.Equal("assistant", last["role"]!.GetValue<string>());
        Assert.Equal(["text", "server_tool_use", "web_search_tool_result"], ClaudeRequest.BlockTypes(last));
        Assert.Equal(2, await _db.ConversationUsage.CountAsync());
    }

    [Fact]
    public async Task NoCitations_NoSourceLineAndNoCrash()
    {
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("Zwykła odpowiedź."));

        var events = await CollectAsync(BuildService(handler, webSearchEnabled: true)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal("Zwykła odpowiedź.", FinalAnswer(events));
        Assert.DoesNotContain("🔗", FinalAnswer(events));
    }

    private static ConversationContext Context() =>
        new(GuildDiscordId, InvokerId, "tester", IsAdmin: false, ChannelId: ThreadChannelId);

    // What the discrete renderer (#274) posts after the last tool round: buffered deltas,
    // dropped on RoundReset, reset at each tool-batch boundary.
    private static string FinalAnswer(IReadOnlyList<ConversationUpdate> events)
    {
        var text = new StringBuilder();
        foreach (var update in events)
        {
            switch (update)
            {
                case ConversationUpdate.AssistantTextDelta delta:
                    text.Append(delta.Text);
                    break;
                case ConversationUpdate.ToolBatchSummary or ConversationUpdate.RoundReset:
                    text.Clear();
                    break;
            }
        }
        return text.ToString();
    }

    private ConversationService BuildService(ScriptedClaudeHandler handler, bool webSearchEnabled)
    {
        var conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            PrimaryGuildId = GuildDiscordId,
            WebSearch = new WebSearchOptions { Enabled = webSearchEnabled },
        });

        var registry = new ConversationToolRegistry(
            MemeSearchTestServices.NewSearch(NewContext(), fixture.ConnectionString),
            new GuildStatsService(NewContext()),
            new DatabaseQueryService(NewContext(), conversationOptions, NullLogger<DatabaseQueryService>.Instance),
            new FakeGuildLiveStateService(),
            new FakeGuildActionService(),
            new FakeConfirmationService(),
            new DatabaseSchemaHint("schema hint"),
            conversationOptions,
            NullLogger<ConversationToolRegistry>.Instance);

        return new ConversationService(
            handler.CreateChatClient(conversationOptions.Value),
            registry,
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            new TestHostEnvironment(),
            NullLogger<ConversationService>.Instance);
    }

    private static async Task<List<ConversationUpdate>> CollectAsync(IAsyncEnumerable<ConversationUpdate> stream)
    {
        List<ConversationUpdate> events = [];
        await foreach (var update in stream)
            events.Add(update);
        return events;
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
