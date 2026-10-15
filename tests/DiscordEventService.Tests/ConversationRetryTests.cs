using System.Diagnostics;
using System.Text;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal("Odpowiedź.", RenderedText(events));

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
        Assert.Equal("Cześć!", RenderedText(events));

        var rows = await _db.ConversationUsage.OrderBy(u => u.Attempt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Failed);
        // The dead stream never sent its final usage: the failed row has no tokens.
        Assert.Null(rows[0].PromptTokens);
        Assert.False(rows[1].Failed);
        Assert.NotNull(rows[1].CostUsd);
    }

    [Fact]
    public async Task RetriesExhausted_PostsVisibleFailureLine_PersistsNoAssistantRows()
    {
        var handler = new ScriptedClaudeHandler(_ => ScriptedClaudeHandler.Status(529));

        var events = await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(options.Value.RetryMaxAttempts, handler.CallCount);
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
        var handler = new ScriptedClaudeHandler(_ => ScriptedClaudeHandler.Status(401));

        var events = await CollectAsync(BuildService(handler, out var options)
            .GenerateReplyAsync("pytanie", Context(), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(options.Value.FailureMessage, RenderedText(events));

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
        Assert.Equal("Po przerwie.", RenderedText(events));
        Assert.True(stopwatch.ElapsedMilliseconds >= 950,
            $"expected the 1s retry-after-ms to be honored, waited only {stopwatch.ElapsedMilliseconds}ms");
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
        ScriptedClaudeHandler handler, out IOptions<ConversationOptions> conversationOptions,
        RecordingLogger? log = null)
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
            handler.CreateChatClient(conversationOptions.Value, log?.For<AnthropicAttemptLogHandler>()),
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
