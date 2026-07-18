using System.ClientModel;
using System.Globalization;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;
using Xunit;

namespace DiscordEventService.Tests;

// The §5 web-search server tool end-to-end (#271): a recording transport UNDER the real
// OpenAI SDK + MEAI adapter proves the wire body (the `openrouter:web_search` entry must
// merge AFTER the adapter-serialized app tools — `Set` would clobber them, and a
// disabled config must leave the body clean), and scripted SSE streams prove citation
// recovery — annotations arrive spread across mid-stream delta chunks at
// `$.choices[0].delta.annotations` — plus the ledger itemisation of search spend.
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
    public async Task RequestBody_WebSearchEnabled_ServerToolAppendedAfterAppTools()
    {
        var transport = new RecordingTransport(_ => SseResponse(SseText("Cześć.")));

        await CollectAsync(BuildService(transport, webSearchEnabled: true)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        var tools = Assert.Single(transport.RequestBodies).GetProperty("tools");
        var entries = tools.EnumerateArray().ToList();
        // The app's function tools survive the patch, and the appended server tool
        // lands after them.
        Assert.Contains(entries, tool => tool.GetProperty("type").GetString() == "function");
        var serverTool = Assert.Single(entries,
            tool => tool.GetProperty("type").GetString() == "openrouter:web_search");
        Assert.Equal("exa", serverTool.GetProperty("parameters").GetProperty("engine").GetString());
        Assert.Equal(3, serverTool.GetProperty("parameters").GetProperty("max_results").GetInt32());
        Assert.Equal("openrouter:web_search", entries[^1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task RequestBody_WebSearchDisabled_NoServerToolOnTheWire()
    {
        var transport = new RecordingTransport(_ => SseResponse(SseText("Cześć.")));

        await CollectAsync(BuildService(transport, webSearchEnabled: false)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        var tools = Assert.Single(transport.RequestBodies).GetProperty("tools");
        Assert.Contains(tools.EnumerateArray(),
            tool => tool.GetProperty("type").GetString() == "function");
        Assert.DoesNotContain(tools.EnumerateArray(),
            tool => tool.GetProperty("type").GetString() == "openrouter:web_search");
    }

    [Fact]
    public async Task AnnotatedMixedTurn_AccumulatesCitationsAcrossRounds_RendersSourceListOnce()
    {
        // Round 0 is the mixed shape the #261 spike observed: narration text, a citation,
        // AND an app tool call in one round. Round 1 answers with a second citation plus
        // a duplicate of the first URL (a later round may re-search) — the source list
        // must dedupe it.
        var transport = new RecordingTransport(call => call switch
        {
            0 => SseResponse(
                AnnotatedTextChunk("Sprawdzam w sieci. ",
                    Annotation("https://example.com/article", "Article"))
                + ToolCallChunk("call_1", "meme_search", """{"query":"zolw","limit":5}""")
                + FinishChunk("tool_calls", UsageJson(cost: 0.02, upstreamCost: 0.012, webSearches: 1))
                + "data: [DONE]\n\n"),
            _ => SseResponse(
                AnnotatedTextChunk("Oto co znalazłem.",
                    Annotation("https://news.example.org/story", "Story"),
                    Annotation("https://example.com/article", "Article"))
                + FinishChunk("stop", UsageJson(cost: 0.015, upstreamCost: 0.01, webSearches: 1))
                + "data: [DONE]\n\n"),
        });

        var events = await CollectAsync(BuildService(transport, webSearchEnabled: true)
            .GenerateReplyAsync("co się dzieje w świecie?", Context(), CancellationToken.None));

        var answer = FinalAnswer(events);
        Assert.StartsWith("Oto co znalazłem.", answer);
        Assert.EndsWith("-# 🔗 [example.com](https://example.com/article) · " +
            "[news.example.org](https://news.example.org/story)", answer);

        // Search spend itemised per round in the ledger: upstream model cost and the
        // search counter recovered from `cost_details` / `server_tool_use_details`.
        var rows = await _db.ConversationUsage.OrderBy(u => u.Round).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.012, rows[0].UpstreamInferenceCostUsd!.Value, precision: 6);
        Assert.Equal(1, rows[0].WebSearchRequests);
        Assert.Equal(0.01, rows[1].UpstreamInferenceCostUsd!.Value, precision: 6);
        Assert.Equal(1, rows[1].WebSearchRequests);
    }

    [Fact]
    public async Task NoAnnotations_NoSourceLineAndNoCrash()
    {
        var transport = new RecordingTransport(_ => SseResponse(SseText("Zwykła odpowiedź.")));

        var events = await CollectAsync(BuildService(transport, webSearchEnabled: true)
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

    private ConversationService BuildService(RecordingTransport transport, bool webSearchEnabled)
    {
        var conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            PrimaryGuildId = GuildDiscordId,
            WebSearch = new WebSearchOptions { Enabled = webSearchEnabled },
        });

        var chatClient = new OpenAIClient(
                new ApiKeyCredential("test-key"),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("http://localhost/api/v1"),
                    Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
                    RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                })
            .GetChatClient(conversationOptions.Value.Model)
            .AsIChatClient();

        var registry = new ConversationToolRegistry(
            new MemeSearchService(NewContext()),
            new GuildStatsService(NewContext()),
            new DatabaseQueryService(NewContext(), conversationOptions, NullLogger<DatabaseQueryService>.Instance),
            new FakeGuildLiveStateService(),
            new FakeGuildActionService(),
            new FakeConfirmationService(),
            new DatabaseSchemaHint("schema hint"),
            conversationOptions,
            NullLogger<ConversationToolRegistry>.Instance);

        return new ConversationService(
            chatClient,
            registry,
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            Options.Create(new OpenRouterOptions { ApiKey = "test-key" }),
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

    private static string SseChunk(string choicesJson, string? extraJson = null) =>
        "data: {\"id\":\"chatcmpl-t\",\"object\":\"chat.completion.chunk\",\"created\":1750000000," +
        $"\"model\":\"anthropic/claude-sonnet-5\",\"choices\":{choicesJson}{(extraJson is null ? "" : "," + extraJson)}}}\n\n";

    private static string SseText(string text) =>
        SseChunk($"[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}},\"finish_reason\":null}}]")
        + FinishChunk("stop", UsageJson(cost: 0.01, upstreamCost: null, webSearches: null))
        + "data: [DONE]\n\n";

    // The annotation shape OpenRouter streams: one `url_citation` per mid-stream delta
    // chunk, offsets always 0.
    private static string Annotation(string url, string title) =>
        $$$"""{"type":"url_citation","url_citation":{"url":"{{{url}}}","title":"{{{title}}}","content":"excerpt","start_index":0,"end_index":0}}""";

    private static string AnnotatedTextChunk(string text, params string[] annotations) =>
        SseChunk($"[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}," +
            $"\"annotations\":[{string.Join(",", annotations)}]}},\"finish_reason\":null}}]");

    private static string ToolCallChunk(string callId, string name, string argumentsJson) =>
        SseChunk($"[{{\"index\":0,\"delta\":{{\"tool_calls\":[{{\"index\":0,\"id\":\"{callId}\",\"type\":\"function\"," +
            $"\"function\":{{\"name\":\"{name}\",\"arguments\":{JsonSerializer.Serialize(argumentsJson)}}}}}]}},\"finish_reason\":null}}]");

    private static string FinishChunk(string finishReason, string usageJson) =>
        SseChunk($"[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"{finishReason}\"}}]", usageJson);

    private static string UsageJson(double cost, double? upstreamCost, int? webSearches)
    {
        var extras = new StringBuilder();
        if (upstreamCost is not null)
            extras.Append($",\"cost_details\":{{\"upstream_inference_cost\":{upstreamCost.Value.ToString(CultureInfo.InvariantCulture)}}}");
        if (webSearches is not null)
            extras.Append($",\"server_tool_use_details\":{{\"web_search_requests\":{webSearches},\"tool_calls_requested\":{webSearches},\"tool_calls_executed\":{webSearches}}}");
        return "\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20,\"total_tokens\":120," +
            $"\"cost\":{cost.ToString(CultureInfo.InvariantCulture)}{extras}}}";
    }

    private static HttpResponseMessage SseResponse(string sse) =>
        new(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };

    // Records every request body (for wire assertions) and returns scripted SSE responses.
    private sealed class RecordingTransport(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _callCount;

        public List<JsonElement> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(JsonDocument.Parse(body).RootElement.Clone());
            return responder(_callCount++);
        }
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
