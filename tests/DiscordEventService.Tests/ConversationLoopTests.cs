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

// Exercises the §2 contract end-to-end against a real Postgres + real MemeSearchService,
// with the model itself faked by a scripted IChatClient. Proves the model->tool->model
// loop: tool-call turn appended before the tool result, the result fed back into the
// next model call, a final answer surfaced, and the round cap terminating the loop.
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

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

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
    public async Task GenerateReplyAsync_ToolCallThenAnswer_FeedsResultBackAndSurfacesAnswer()
    {
        const string finalAnswer = "Found the turtle meme for you.";
        var client = new ScriptedChatClient((callIndex, _, _) => callIndex switch
        {
            0 => ToolCall("call_1", "meme_search", new Dictionary<string, object?> { ["query"] = "zolw", ["limit"] = 5 }),
            _ => FinalText(finalAnswer),
        });

        var service = BuildService(client);
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester");

        var reply = await service.GenerateReplyAsync("znajdz mema o zolwiu", context, CancellationToken.None);

        Assert.Equal(finalAnswer, reply);

        // Two model calls: the tool-requesting round, then the answering round.
        Assert.Equal(2, client.Calls.Count);
        // Tools are re-sent on every round.
        Assert.Equal([true, true], client.CallHadTools);

        // The second call's transcript carries the tool result, and the assistant
        // tool-call turn precedes it.
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
    }

    [Fact]
    public async Task GenerateReplyAsync_ModelNeverStops_HitsRoundCapAndFinalizesWithoutTools()
    {
        const int cap = 3;
        const string cappedAnswer = "Okay, stopping here.";
        // Always ask for a tool while tools are offered; answer once they're withheld.
        var client = new ScriptedChatClient((callIndex, _, options) =>
            options?.Tools is { Count: > 0 }
                ? ToolCall($"call_{callIndex}", "meme_search", new Dictionary<string, object?> { ["query"] = "x", ["limit"] = 1 })
                : FinalText(cappedAnswer));

        var service = BuildService(client, maxToolRounds: cap);
        var context = new ConversationContext(GuildDiscordId, InvokerId: 42UL, "tester");

        var reply = await service.GenerateReplyAsync("loop forever", context, CancellationToken.None);

        Assert.Equal(cappedAnswer, reply);
        // cap rounds offering tools, then one finalizing call with tools withheld.
        Assert.Equal(cap + 1, client.Calls.Count);
        Assert.Equal([true, true, true, false], client.CallHadTools);
    }

    private ConversationService BuildService(IChatClient client, int maxToolRounds = 8)
    {
        var conversationOptions = Options.Create(new ConversationOptions
        {
            MaxToolRounds = maxToolRounds,
            ReasoningEffort = "low",
        });
        var openRouterOptions = Options.Create(new OpenRouterOptions { ApiKey = "test-key" });

        var registry = new ConversationToolRegistry(
            new MemeSearchService(NewContext()),
            conversationOptions,
            NullLogger<ConversationToolRegistry>.Instance);

        return new ConversationService(
            client,
            registry,
            conversationOptions,
            openRouterOptions,
            NullLogger<ConversationService>.Instance);
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

    private static ChatResponse ToolCall(string callId, string name, Dictionary<string, object?> arguments) =>
        new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]));

    private static ChatResponse FinalText(string text) =>
        new ChatResponse(new ChatMessage(ChatRole.Assistant, text));

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
            DescriptionPl = "Unikatowy żółw na deskorolce",
            DescriptionEn = "A unique turtle on a skateboard",
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

    // A fake IChatClient that returns scripted responses and records each call's
    // transcript and whether tools were offered.
    private sealed class ScriptedChatClient(
        Func<int, IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> responder) : IChatClient
    {
        private int _callIndex;

        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];
        public List<bool> CallHadTools { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var snapshot = messages.ToList();
            Calls.Add(snapshot);
            CallHadTools.Add(options?.Tools is { Count: > 0 });
            return Task.FromResult(responder(_callIndex++, snapshot, options));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Streaming lands in §3.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
