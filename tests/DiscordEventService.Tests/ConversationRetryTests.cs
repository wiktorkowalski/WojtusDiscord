using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
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

// The §2 per-round retry policy end-to-end (#268): fault-injecting transport UNDER the
// real OpenAI SDK + MEAI adapter — the same seam as the byte-fidelity tests — so both
// empirically probed mid-stream surfaces (B-OQ1) are exercised as the SDK actually
// produces them: the finish_reason:"error" frame throws an unknown-finish-reason
// ArgumentOutOfRangeException; a frame without it passes through silently with `$.error`
// on the raw patch. Ledger rows per attempt land in a real Postgres.
public sealed class ConversationRetryTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 31UL;
    private const ulong ThreadChannelId = 33UL;
    private const ulong InvokerId = 44UL;

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
    public async Task PreStream500_RetriesWithBackoff_AnswersAndLedgersFailedAttempt()
    {
        var transport = new ScriptedTransport(call => call switch
        {
            0 => ErrorResponse(HttpStatusCode.InternalServerError),
            _ => SseResponse(SseText("Odpowiedź.")),
        });

        var events = await CollectAsync(BuildService(transport, out _)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(2, transport.CallCount);
        Assert.Equal("Odpowiedź.", RenderedText(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(1, row.Round));
        Assert.True(rows[0].Failed);
        Assert.Equal(1, rows[0].Attempt);
        Assert.False(rows[1].Failed);
        Assert.Equal(2, rows[1].Attempt);

        // The failed attempt persisted NO transcript rows — only the ledger row.
        Assert.Equal(2, await _db.ConversationMessages.CountAsync()); // user + winning answer
    }

    [Fact]
    public async Task MidStreamErrorFrame_FinishReasonError_RetriesAndRendersAnswerExactlyOnce()
    {
        var transport = new ScriptedTransport(call => call switch
        {
            // Partial text, then OpenRouter's documented error frame — the SDK throws an
            // unknown-finish-reason ArgumentOutOfRangeException on this surface.
            0 => SseResponse(SsePartialTextThenErrorFrame("Czę", finishReasonError: true)),
            _ => SseResponse(SseText("Cześć!")),
        });

        var events = await CollectAsync(BuildService(transport, out _)
            .GenerateReplyAsync("hej", Context(), CancellationToken.None));

        Assert.Equal(2, transport.CallCount);
        // The partial delta reached the renderer, so a RoundReset must discard it.
        Assert.Contains(events, e => e is ConversationUpdate.RoundReset);
        Assert.Equal("Cześć!", RenderedText(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Failed);
        Assert.False(rows[1].Failed);
    }

    [Fact]
    public async Task MidStreamErrorFrame_Silent_DetectedInsteadOfEmptyAnswerFallback()
    {
        var transport = new ScriptedTransport(call => call switch
        {
            // The silent surface: top-level `error`, finish_reason null — deserializes as
            // an empty update; without detection this misfires the empty-answer fallback.
            0 => SseResponse(SsePartialTextThenErrorFrame("", finishReasonError: false)),
            _ => SseResponse(SseText("Działa.")),
        });

        var events = await CollectAsync(BuildService(transport, out _)
            .GenerateReplyAsync("hej", Context(), CancellationToken.None));

        Assert.Equal(2, transport.CallCount);
        Assert.Equal("Działa.", RenderedText(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Failed);
        Assert.False(rows[1].Failed);
    }

    [Fact]
    public async Task RetriesExhausted_PostsVisibleFailureLine_PersistsNoAssistantRows()
    {
        var transport = new ScriptedTransport(_ => ErrorResponse(HttpStatusCode.BadGateway));

        var events = await CollectAsync(BuildService(transport, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(options.Value.RetryMaxAttempts, transport.CallCount);
        Assert.Equal(options.Value.FailureMessage, RenderedText(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.True(row.Failed));
        Assert.Equal([1, 2, 3], rows.Select(r => r.Attempt));

        // Only the user message reached the transcript — a failed turn appends nothing else.
        var message = await _db.ConversationMessages.SingleAsync();
        Assert.Equal("pytanie", message.Text);
    }

    [Fact]
    public async Task TerminalStatus401_DoesNotRetry_SurfacesFailureLine()
    {
        var transport = new ScriptedTransport(_ => ErrorResponse(HttpStatusCode.Unauthorized));

        var events = await CollectAsync(BuildService(transport, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(1, transport.CallCount);
        Assert.Equal(options.Value.FailureMessage, RenderedText(events));

        var row = await _db.ConversationUsage.SingleAsync();
        Assert.True(row.Failed);
        Assert.Equal(1, row.Attempt);
    }

    [Fact]
    public async Task RateLimited_RetryAfterHeader_OutweighsJitterBackoff()
    {
        var transport = new ScriptedTransport(call => call switch
        {
            0 => ErrorResponse(HttpStatusCode.TooManyRequests, retryAfterSeconds: 1),
            _ => SseResponse(SseText("Po przerwie.")),
        });

        // RetryBaseDelayMs=1 in BuildService: any wait ≥ the header value proves the
        // header won over the (sub-hundred-ms) jitter.
        var stopwatch = Stopwatch.StartNew();
        var events = await CollectAsync(BuildService(transport, out _)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(2, transport.CallCount);
        Assert.Equal("Po przerwie.", RenderedText(events));
        Assert.True(stopwatch.ElapsedMilliseconds >= 950,
            $"expected the 1s Retry-After to be honored, waited only {stopwatch.ElapsedMilliseconds}ms");
    }

    private static ConversationContext Context() =>
        new(GuildDiscordId, InvokerId, "tester", IsAdmin: false, ChannelId: ThreadChannelId);

    // What the discrete renderer (#274) would post: deltas buffered, RoundReset drops
    // the current round's buffer, the remainder is the answer.
    private static string RenderedText(IReadOnlyList<ConversationUpdate> events)
    {
        var text = new StringBuilder();
        foreach (var update in events)
        {
            switch (update)
            {
                case ConversationUpdate.AssistantTextDelta delta:
                    text.Append(delta.Text);
                    break;
                case ConversationUpdate.RoundReset:
                    text.Clear();
                    break;
            }
        }
        return text.ToString();
    }

    private ConversationService BuildService(
        ScriptedTransport transport, out IOptions<ConversationOptions> conversationOptions)
    {
        conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            PrimaryGuildId = GuildDiscordId,
            // Keep the exponential backoff sub-hundred-ms in tests; the Retry-After test
            // proves the header outweighs it.
            RetryBaseDelayMs = 1,
            RetryMaxDelayMs = 2,
        });

        var chatClient = new OpenAIClient(
                new ApiKeyCredential("test-key"),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri("http://localhost/api/v1"),
                    Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
                    // Mirror prod (ConversationRegistration): the app-level §2 policy is
                    // the single owner of retries.
                    RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                })
            .GetChatClient(conversationOptions.Value.Model)
            .AsIChatClient();

        return new ConversationService(
            chatClient,
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

    private const string UsageJson =
        "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15,\"cost\":0.01}";

    private static string SseChunk(string choicesJson, string? extraJson = null) =>
        "data: {\"id\":\"chatcmpl-t\",\"object\":\"chat.completion.chunk\",\"created\":1750000000," +
        $"\"model\":\"anthropic/claude-sonnet-5\",\"choices\":{choicesJson}{(extraJson is null ? "" : "," + extraJson)}}}\n\n";

    private static string SseText(string text)
    {
        var delta = JsonSerializer.Serialize(text);
        return SseChunk($"[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"content\":{delta}}},\"finish_reason\":null}}]")
            + SseChunk("[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]", UsageJson)
            + "data: [DONE]\n\n";
    }

    // OpenRouter's documented mid-stream error delivery: optional partial text, then a
    // normal data chunk with top-level `error`; the stream ends after it (no [DONE]).
    // finishReasonError toggles the two SDK surfaces probed for B-OQ1.
    private static string SsePartialTextThenErrorFrame(string partialText, bool finishReasonError)
    {
        var text = partialText.Length == 0
            ? string.Empty
            : SseChunk($"[{{\"index\":0,\"delta\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(partialText)}}},\"finish_reason\":null}}]");
        var finishReason = finishReasonError ? "\"error\"" : "null";
        return text + SseChunk(
            $"[{{\"index\":0,\"delta\":{{\"content\":\"\"}},\"finish_reason\":{finishReason}}}]",
            "\"error\":{\"code\":502,\"message\":\"Provider returned error\",\"metadata\":{\"provider_name\":\"Anthropic\"}}");
    }

    private static HttpResponseMessage SseResponse(string sse) =>
        new(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode status, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $"{{\"error\":{{\"code\":{(int)status},\"message\":\"scripted failure\"}}}}",
                Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is not null)
            response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString());
        return response;
    }

    private sealed class ScriptedTransport(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(CallCount++));
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
