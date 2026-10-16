using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// Calls the real Claude API (#426) and costs a few cents per run, so it is skipped unless a
// key is set: ANTHROPIC_API_KEY in the process environment, or ANTHROPIC_API_KEY /
// Anthropic__ApiKey in the repo .env file (found by walking up from the test output
// directory; read without setting environment variables, so the other secrets stay out):
//   ANTHROPIC_API_KEY=sk-ant-... dotnet test --filter "FullyQualifiedName~ConversationLiveApiTests"
// In CI it runs only in the manual workflow .github/workflows/live-tests.yml.
// It checks what a scripted transport cannot: that the API accepts what this bot sends —
// thinking blocks and server-tool blocks sent back, strict tools, tool_choice none with tool
// history, the prompt cache — on the production pipeline (ConversationRegistration).
public sealed class ConversationLiveApiTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 77UL;
    private const ulong ChannelDiscordId = 78UL;
    private const ulong MemeMessageId = 7901UL;

    private static readonly string[] KeyNames = [AnthropicOptions.ApiKeyEnvironmentVariable, "Anthropic__ApiKey"];

    private static readonly Lazy<string?> LazyApiKey = new(() =>
        KeyNames.Select(Environment.GetEnvironmentVariable).FirstOrDefault(key => !string.IsNullOrEmpty(key))
        ?? DotNetEnv.Env.NoEnvVars().TraversePath().Load()
            .Where(pair => KeyNames.Contains(pair.Key) && pair.Value.Length > 0)
            .Select(pair => pair.Value)
            .FirstOrDefault());

    private static string? ApiKey => LazyApiKey.Value;

    private DiscordDbContext _db = null!;
    // The test database outlives a run: a random base keeps every run's conversations apart.
    private ulong _channelId = (ulong)Random.Shared.NextInt64(1_000_000, 1_000_000_000_000);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        if (!await _db.Guilds.AnyAsync(g => g.DiscordId == GuildDiscordId))
            await SeedCatMemeAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // Model → meme_search → model, with thinking on: round 2 sends round 1's thinking block
    // back, and reads the cache round 1 wrote.
    [LiveFact]
    public async Task ToolLoop_IsAccepted_AndRoundTwoReadsTheCache()
    {
        var (service, log, channel) = BuildService();

        var events = await CollectAsync(service, "znajdź mema o kocie i powiedz krótko co to za mem", channel);

        Assert.Contains(events, e => e is ConversationUpdate.ToolCall);
        var answer = Answer(events);
        Assert.False(string.IsNullOrWhiteSpace(answer), "expected a non-empty streamed answer");
        Assert.DoesNotContain(new ConversationOptions().FailureMessage, answer);

        var rows = await UsageRowsAsync(channel);
        Assert.True(rows.Count >= 2, Dump(log));
        Assert.All(rows, row => Assert.False(row.Failed));
        Assert.True(rows[1].CacheReadTokens > 0, $"no cache read in round 2: {Dump(log)}");
        Assert.All(rows, row => Assert.True(row.CostUsd > 0));
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [LiveFact]
    public async Task WebSearch_AnswersWithASourceList()
    {
        var (service, log, channel) = BuildService();

        var events = await CollectAsync(service,
            "Search the web: what is the latest stable version of the .NET SDK? Answer in one sentence and cite the source.",
            channel);

        var answer = Answer(events);
        Assert.Contains("🔗", answer);
        Assert.Contains(events, e => e is ConversationUpdate.ToolCall { Line: var line } && line.StartsWith("-# 🔎"));
        var rows = await UsageRowsAsync(channel);
        Assert.True(rows.Sum(row => row.WebSearchRequests ?? 0) > 0, Dump(log));
        // The search fee is on top of the token cost.
        Assert.Contains(rows, row => row.CostUsd > row.UpstreamInferenceCostUsd);
    }

    // A web search and a local tool in one turn: the server-tool blocks of a round go back with
    // its tool_use and the tool result in the next request.
    [LiveFact]
    public async Task SearchThenLocalTool_RoundTripsTheServerToolBlocks()
    {
        var (service, log, channel) = BuildService();

        var events = await CollectAsync(service,
            "First search the web for what a 'grumpy cat' meme is, then call meme_search for a cat meme on this server. "
            + "Answer in two short sentences.",
            channel);

        Assert.Contains(events, e => e is ConversationUpdate.ToolCall);
        Assert.Contains(events, e => e is ConversationUpdate.ToolCall { Line: var line } && line.StartsWith("-# 🔎"));
        Assert.DoesNotContain(new ConversationOptions().FailureMessage, Answer(events));
        var rows = await UsageRowsAsync(channel);
        Assert.True(rows.Sum(row => row.WebSearchRequests ?? 0) > 0, Dump(log));
        Assert.All(rows, row => Assert.False(row.Failed, Dump(log)));
    }

    // The dev-bot prompt that failed with a 400 (#426): the model calls web_search and
    // meme_search in one group, so the API defers the search and its result opens the next
    // response. That result must go back on round 3.
    [LiveFact]
    public async Task SearchNextToLocalTool_DeferredSearchResult_IsSentBack()
    {
        var (service, log, channel) = BuildService();

        var events = await CollectAsync(service,
            "wyszukaj w necie jaka jest najnowsza wersja .NET, a potem znajdź mema o programowaniu", channel);

        Assert.DoesNotContain(new ConversationOptions().FailureMessage, Answer(events));
        var rows = await UsageRowsAsync(channel);
        Assert.All(rows, row => Assert.False(row.Failed, Dump(log)));
        Assert.True(rows.Sum(row => row.WebSearchRequests ?? 0) > 0, Dump(log));
    }

    // MaxToolRounds 1: the second call is the post-cap round — tool history, the same tools
    // declared, tool_choice none, round 1's thinking still in the transcript.
    [LiveFact]
    public async Task RoundCap_ToolChoiceNone_WithToolHistory_IsAccepted()
    {
        var (service, log, channel) = BuildService(maxToolRounds: 1);

        var events = await CollectAsync(service, "Call meme_search for 'kot', then tell me what you found.", channel);

        var answer = Answer(events);
        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.DoesNotContain(new ConversationOptions().FailureMessage, answer);
        var rows = await UsageRowsAsync(channel);
        Assert.True(rows.Count(row => !row.Failed) == 2,
            $"rows {string.Join(",", rows.Select(r => $"{r.Round}/{r.Attempt}/{r.Failed}"))}; {Dump(log)}");
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Error);
    }

    private (ConversationService Service, RecordingLogger Log, ulong Channel) BuildService(int maxToolRounds = 8)
    {
        var log = new RecordingLogger();
        var conversationOptions = Options.Create(new ConversationOptions
        {
            ReasoningEffort = "low",
            MaxToolRounds = maxToolRounds,
            PrimaryGuildId = GuildDiscordId,
        });

        var chatClient = ConversationRegistration.BuildChatClient(
            ConversationRegistration.CreateApiClient(ApiKey!, log.For<AnthropicAttemptLogHandler>()),
            conversationOptions.Value, enableSensitiveData: false);

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
        var memory = new ConversationMemoryService(
            NewContext(), conversationOptions, NullLogger<ConversationMemoryService>.Instance);
        var service = new ConversationService(
            chatClient, registry, memory, conversationOptions,
            Options.Create(new AnthropicOptions { ApiKey = ApiKey }),
            new TestHostEnvironment(), log.For<ConversationService>());

        // A channel of its own per test: each one is a fresh conversation.
        return (service, log, Interlocked.Increment(ref _channelId));
    }

    private static async Task<List<ConversationUpdate>> CollectAsync(
        ConversationService service, string question, ulong channel)
    {
        var context = new ConversationContext(GuildDiscordId, InvokerId: 1UL, "tester", IsAdmin: false, ChannelId: channel);
        List<ConversationUpdate> events = [];
        await foreach (var update in service.GenerateReplyAsync(question, context, CancellationToken.None))
            events.Add(update);
        return events;
    }

    private static string Answer(IEnumerable<ConversationUpdate> events) =>
        string.Concat(events.OfType<ConversationUpdate.AssistantTextDelta>().Select(d => d.Text));

    private async Task<List<Data.Entities.Conversations.ConversationUsageEntity>> UsageRowsAsync(ulong channel)
    {
        await using var db = NewContext();
        return await db.ConversationUsage
            .Where(u => u.Conversation.ChannelDiscordId == channel)
            .OrderBy(u => u.Round).ThenBy(u => u.Attempt)
            .ToListAsync();
    }

    private static string Dump(RecordingLogger log) =>
        string.Join(" | ", log.Entries.Select(e => $"{e.Level}: {e.Message}"));

    private async Task SeedCatMemeAsync()
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
        var author = new UserEntity { DiscordId = 9UL, Username = "u" };
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
            AttachmentDiscordId = 91UL,
            FileName = "cat.png",
            FileSizeBytes = 1234,
            ContentType = "image/png",
            ContentHash = "hash-cat",
            Status = MemeIndexStatus.Indexed,
            Annotations =
            [
                new MemeAnnotationEntity
                {
                    AttachmentDiscordId = 91UL,
                    ModelId = "google/gemini-3-flash-preview",
                    PromptVersion = OpenRouterClient.PromptVersion,
                    IndexedAtUtc = DateTime.UtcNow,
                    DescriptionPl = "Zły kot patrzy w kamerę",
                    DescriptionEn = "Grumpy cat staring at the camera",
                    OcrText = "",
                    Tags = ["kot"],
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

    private sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (string.IsNullOrEmpty(ApiKey))
                Skip = "Set ANTHROPIC_API_KEY to run the tests against the real Claude API.";
        }
    }
}
