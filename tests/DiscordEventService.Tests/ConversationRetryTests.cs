using System.Diagnostics;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The §2 per-round retry policy end-to-end (#268, #426): a fault-injecting transport UNDER
// the real AnthropicClient + MEAI adapter, so each failure surface is exercised the way the
// SDK actually produces it — an HTTP status before the stream, an `error` event in the middle
// of the stream, a 429 with retry-after-ms. The SDK retry is off; the loop owns retries, and
// ledger rows per attempt land in a real Postgres.
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
        var handler = new ScriptedClaudeHandler(call => call switch
        {
            0 => ScriptedClaudeHandler.Status(500),
            _ => ScriptedClaudeHandler.Sse(ClaudeSse.TextAnswer("Odpowiedź.")),
        });
        var log = new RecordingLogger();

        var events = await CollectAsync(BuildService(handler, out _, log)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal("Odpowiedź.", await TurnRendering.FinalAnswerAsync(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(1, row.Round));
        Assert.True(rows[0].Failed);
        Assert.Equal(1, rows[0].Attempt);
        // A status before the stream reports no usage.
        Assert.Null(rows[0].PromptTokens);
        Assert.Null(rows[0].CostUsd);
        Assert.False(rows[1].Failed);
        Assert.Equal(2, rows[1].Attempt);

        // The failed attempt persisted NO transcript rows — only the ledger row.
        Assert.Equal(2, await _db.ConversationMessages.CountAsync()); // user + winning answer

        // The handler names the status and the request id; the loop names the retry.
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("failed with status 500") && e.Message.Contains("req_test"));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("retrying in"));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("OpenRouter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MidStreamErrorEvent_RetriesAndRendersAnswerExactlyOnce()
    {
        var handler = ScriptedClaudeHandler.Streams(call => call switch
        {
            // Partial text, then an `error` event: the SDK throws AnthropicSseException.
            0 => ClaudeSse.Start().Text("Czę").Error("overloaded_error"),
            _ => ClaudeSse.TextAnswer("Cześć!"),
        });

        var events = await CollectAsync(BuildService(handler, out _)
            .GenerateReplyAsync("hej", Context(), CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
        // The partial delta reached the renderer, so a RoundReset must discard it.
        Assert.Contains(events, e => e is ConversationUpdate.RoundReset);
        Assert.Equal("Cześć!", await TurnRendering.FinalAnswerAsync(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Failed);
        // The dead stream never sent its final usage, but message_start billed the input:
        // the failed row keeps it (and its cost); the output count is unknown.
        Assert.Equal(100, rows[0].PromptTokens);
        Assert.Null(rows[0].CompletionTokens);
        Assert.Equal(0.0002, rows[0].CostUsd!.Value, precision: 9);
        Assert.False(rows[1].Failed);
        Assert.NotNull(rows[1].CostUsd);
    }

    // #451: the SDK ends the enumeration when the body ends. A stream closed cleanly before
    // message_stop is a failed attempt: retried, never posted or persisted as the answer.
    [Fact]
    public async Task StreamEndsWithoutMessageStop_IsRetried_NotPostedAsFinal()
    {
        var handler = ScriptedClaudeHandler.Streams(call => call switch
        {
            0 => ClaudeSse.Start().Text("Ucięta odpo").ToString(),
            _ => ClaudeSse.TextAnswer("Pełna odpowiedź."),
        });

        var events = await CollectAsync(BuildService(handler, out _)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
        Assert.Contains(events, e => e is ConversationUpdate.RoundReset);
        Assert.Equal("Pełna odpowiedź.", await TurnRendering.FinalAnswerAsync(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Failed);
        Assert.Equal(100, rows[0].PromptTokens);
        Assert.False(rows[1].Failed);
        Assert.Equal(["pytanie", "Pełna odpowiedź."],
            await _db.ConversationMessages.OrderBy(m => m.Id).Select(m => m.Text).ToListAsync());
    }

    [Fact]
    public async Task RetriesExhausted_PostsVisibleFailureLine_PersistsNoAssistantRows()
    {
        var handler = new ScriptedClaudeHandler(_ => ScriptedClaudeHandler.Status(529));

        var events = await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(options.Value.RetryMaxAttempts, handler.CallCount);
        Assert.Equal(options.Value.FailureMessage, await TurnRendering.FinalAnswerAsync(events));

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
        var handler = new ScriptedClaudeHandler(_ => ScriptedClaudeHandler.Status(401));

        var events = await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(options.Value.FailureMessage, await TurnRendering.FinalAnswerAsync(events));

        var row = await _db.ConversationUsage.SingleAsync();
        Assert.True(row.Failed);
        Assert.Equal(1, row.Attempt);
    }

    [Fact]
    public async Task RateLimited_RetryAfterMs_OutweighsJitterBackoff()
    {
        var handler = new ScriptedClaudeHandler(call => call switch
        {
            0 => ScriptedClaudeHandler.Status(429, retryAfterMs: 1000),
            _ => ScriptedClaudeHandler.Sse(ClaudeSse.TextAnswer("Po przerwie.")),
        });

        // RetryBaseDelayMs=1 in BuildService: any wait ≥ the header value proves the
        // header won over the (sub-hundred-ms) jitter.
        var stopwatch = Stopwatch.StartNew();
        var events = await CollectAsync(BuildService(handler, out _)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(2, handler.CallCount);
        Assert.Equal("Po przerwie.", await TurnRendering.FinalAnswerAsync(events));
        Assert.True(stopwatch.ElapsedMilliseconds >= 950,
            $"expected the 1s retry-after-ms to be honored, waited only {stopwatch.ElapsedMilliseconds}ms");
    }

    // #445: a wait past the turn budget would be cut by the turn token after RoundReset emptied
    // the buffer, leaving silence. The round gives up at once and posts the failure line.
    [Fact]
    public async Task RateLimited_RetryAfterPastTheTurnBudget_FailsAtOnceWithTheFailureLine()
    {
        var handler = new ScriptedClaudeHandler(_ => ScriptedClaudeHandler.Status(429, retryAfterMs: 300_000));

        var stopwatch = Stopwatch.StartNew();
        var events = await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(options.Value.FailureMessage, await TurnRendering.FinalAnswerAsync(events));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"waited {stopwatch.ElapsedMilliseconds}ms");
        Assert.True((await _db.ConversationUsage.SingleAsync()).Failed);
    }

    // #445: the paused round's text waits in the buffer; the failure line goes on its own line,
    // or Discord renders the `-#` literally.
    [Fact]
    public async Task FailureAfterAPausedRound_PutsTheFailureLineOnItsOwnLine()
    {
        var handler = new ScriptedClaudeHandler(call => call switch
        {
            0 => ScriptedClaudeHandler.Sse(ClaudeSse.Start().Text("Szukam dalej…").End("pause_turn")),
            _ => ScriptedClaudeHandler.Status(401),
        });

        var events = await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal([$"Szukam dalej…\n{options.Value.FailureMessage}"], await TurnRendering.RenderAsync(events));
    }

    // The SDK attempt timeout (ClientOptions.Timeout) is a linked CancellationTokenSource over
    // the send; the SDK rethrows its expiry as TaskCanceledException wrapping TimeoutException.
    // Pinned here on the real SDK + MEAI adapter, so an SDK change of shape shows up.
    [Fact]
    public async Task AttemptTimeout_RealSdk_ThrowsTaskCanceledWrappingTimeout()
    {
        var handler = new ScriptedClaudeHandler((_, token) => ScriptedClaudeHandler.Hang(token));
        var client = handler.CreateChatClient(
            new ConversationOptions(), attemptTimeout: TimeSpan.FromMilliseconds(200));

        var thrown = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hej")]))
            {
            }
        });

        Assert.IsType<TimeoutException>(thrown.InnerException);
    }

    // #426 review: a hung attempt runs out the SDK attempt timeout and is retried.
    [Fact]
    public async Task AttemptTimeout_IsRetried_AnswersOnTheNextAttempt()
    {
        var handler = new ScriptedClaudeHandler((call, token) => call switch
        {
            0 => ScriptedClaudeHandler.Hang(token),
            _ => Task.FromResult(ScriptedClaudeHandler.Sse(ClaudeSse.TextAnswer("Po timeoucie."))),
        });

        var events = await CollectAsync(BuildService(handler, out _, attemptTimeout: TimeSpan.FromMilliseconds(200))
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal("Po timeoucie.", await TurnRendering.FinalAnswerAsync(events));
        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal([true, false], rows.Select(r => r.Failed));
    }

    // The caller's own cancellation is never a transient fault: the turn ends, no retry.
    [Fact]
    public async Task CallerCancel_WhileAttemptHangs_EndsTheTurnWithoutRetry()
    {
        using var caller = new CancellationTokenSource();
        // The caller cancels once the attempt is on the wire, well inside the 60 s attempt timeout.
        var handler = new ScriptedClaudeHandler((_, token) =>
        {
            caller.CancelAfter(TimeSpan.FromMilliseconds(100));
            return ScriptedClaudeHandler.Hang(token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(
            BuildService(handler, out _).GenerateReplyAsync("pytanie", Context(), caller.Token)));

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(await _db.ConversationUsage.ToListAsync());
    }

    // #452: a stream that billed its input and then stalled until the turn was cut. The
    // cancelled attempt still gets its failed ledger row.
    [Fact]
    public async Task CallerCancel_AfterMessageStart_LedgersTheBilledInput()
    {
        using var caller = new CancellationTokenSource();
        var handler = new ScriptedClaudeHandler((_, _) =>
        {
            caller.CancelAfter(TimeSpan.FromMilliseconds(200));
            return Task.FromResult(ScriptedClaudeHandler.SseThenHang(ClaudeSse.Start().Text("Czę").ToString(), caller.Token));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(
            BuildService(handler, out _).GenerateReplyAsync("pytanie", Context(), caller.Token)));

        Assert.Equal(1, handler.CallCount);
        var row = Assert.Single(await _db.ConversationUsage.ToListAsync());
        Assert.True(row.Failed);
        Assert.Equal(100, row.PromptTokens);
        Assert.Null(row.CompletionTokens);
        Assert.NotNull(row.CostUsd);
    }

    // #452: a consumer that stops reading (a failed send) ends the attempt the same way.
    [Fact]
    public async Task ConsumerStopsReading_MidStream_LedgersTheBilledInput()
    {
        var handler = new ScriptedClaudeHandler((_, token) =>
            Task.FromResult(ScriptedClaudeHandler.SseThenHang(ClaudeSse.Start().Text("Czę").ToString(), token)));

        await foreach (var update in BuildService(handler, out _)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None))
        {
            if (update is ConversationUpdate.AssistantTextDelta)
                break;
        }

        var row = Assert.Single(await _db.ConversationUsage.ToListAsync());
        Assert.True(row.Failed);
        Assert.Equal(100, row.PromptTokens);
    }

    // #452: an attempt that searched and then died billed the search; the failed row has its fee.
    [Fact]
    public async Task MidStreamError_AfterASearch_LedgersTheSearchFee()
    {
        var handler = ScriptedClaudeHandler.Streams(call => call switch
        {
            0 => ClaudeSse.Start()
                .WebSearch("srvtoolu_1", "pogoda", ("https://weather.example/krk", "Weather"))
                .Error("overloaded_error"),
            _ => ClaudeSse.TextAnswer("Słonecznie."),
        });

        await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pogoda?", Context(), CancellationToken.None));

        var failed = await _db.ConversationUsage.SingleAsync(u => u.Failed);
        Assert.Equal(1, failed.WebSearchRequests);
        Assert.Equal(options.Value.WebSearchPerThousandUsd / 1000d, failed.CostUsd!.Value - failed.UpstreamInferenceCostUsd!.Value, precision: 9);
    }

    private static ConversationContext Context() =>
        new(GuildDiscordId, InvokerId, "tester", IsAdmin: false, ChannelId: ThreadChannelId);

    private ConversationService BuildService(
        ScriptedClaudeHandler handler, out IOptions<ConversationOptions> conversationOptions,
        RecordingLogger? log = null, TimeSpan? attemptTimeout = null)
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

        return new ConversationService(
            handler.CreateChatClient(conversationOptions.Value, log?.For<AnthropicAttemptLogHandler>(), attemptTimeout),
            BuildRegistry(conversationOptions),
            new ConversationMemoryService(NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance),
            conversationOptions,
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            new TestHostEnvironment(),
            log?.For<ConversationService>() ?? NullLogger<ConversationService>.Instance);
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

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
