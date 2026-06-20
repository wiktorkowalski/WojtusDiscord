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
using System.ClientModel;
using System.Diagnostics;
using Xunit;

namespace DiscordEventService.Tests;

// MANUAL live-gate test (#241): drives the real streaming agentic loop against OpenRouter
// -> Anthropic with reasoning ON and a forced multi-round tool call (meme_search hits the
// seeded row, so the model must do model -> tool -> model). It closes the carried-forward
// risk that the OpenAI/MEAI adapter doesn't replay reasoning_details (Anthropic 400 on
// round 2) AND that the real usage.cost is recovered from the experimental ChatTokenUsage
// patch. Both verified live 2026-06-20 (no 400; cost ≈ $0.009). Skipped in CI (hits a paid
// API); run by hand with OPENROUTER_API_KEY exported.
public sealed class ConversationLiveApiTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 77UL;
    private const ulong ChannelDiscordId = 78UL;
    private const ulong MemeMessageId = 7901UL;

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await SeedCatMemeAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact(Skip = "Live: hits real OpenRouter (paid). Run manually to verify the reasoning+multi-round tool gate and usage.cost recovery.")]
    public async Task GenerateReplyAsync_RealOpenRouter_MultiRoundToolCall_DoesNotThrow()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(apiKey), "export OPENROUTER_API_KEY to run this live test");

        // Listen to the turn span so we can assert the real usage.cost was recovered from
        // the experimental ChatTokenUsage patch and recorded — otherwise StartActivity
        // returns null (no listener) and the cost path is silently untested.
        double? capturedCost = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == ConversationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("conversation.cost_usd") is double cost)
                    capturedCost = cost;
            },
        };
        ActivitySource.AddActivityListener(listener);

        var conversationOptions = Options.Create(new ConversationOptions
        {
            Model = "anthropic/claude-sonnet-4.6",
            ReasoningEffort = "medium", // reasoning ON — the gate condition
            MaxToolRounds = 8,
            PrimaryGuildId = GuildDiscordId,
        });
        var openRouterOptions = Options.Create(new OpenRouterOptions { ApiKey = apiKey });

        var chatClient = new OpenAIClient(
                new ApiKeyCredential(apiKey!),
                new OpenAIClientOptions { Endpoint = new Uri(openRouterOptions.Value.BaseUrl) })
            .GetChatClient(conversationOptions.Value.Model)
            .AsIChatClient()
            .AsBuilder()
            .Build();

        var registry = new ConversationToolRegistry(
            new MemeSearchService(NewContext()),
            conversationOptions,
            NullLogger<ConversationToolRegistry>.Instance);
        var service = new ConversationService(
            chatClient, registry, conversationOptions, openRouterOptions, NullLogger<ConversationService>.Instance);

        var context = new ConversationContext(GuildDiscordId, InvokerId: 1UL, "tester");

        List<ConversationUpdate> events = [];
        await foreach (var update in service.GenerateReplyAsync(
            "znajdź mema o kocie i powiedz krótko co to za mem", context, CancellationToken.None))
        {
            events.Add(update);
        }

        // The model survived the second round (no Anthropic 400 on replayed reasoning) and
        // produced both a tool batch and a non-empty answer.
        Assert.Contains(events, e => e is ConversationUpdate.ToolBatchSummary);
        var answer = string.Concat(events.OfType<ConversationUpdate.AssistantTextDelta>().Select(d => d.Text));
        Assert.False(string.IsNullOrWhiteSpace(answer), "expected a non-empty streamed answer");

        // The real OpenRouter usage.cost flowed through the experimental ChatTokenUsage
        // patch and was recorded on the turn span (verified live 2026-06-20 ≈ $0.009).
        Assert.True(capturedCost is > 0, $"expected a non-zero usage.cost; got {capturedCost?.ToString() ?? "NULL"}");
    }

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
            DescriptionPl = "Zły kot patrzy w kamerę",
            DescriptionEn = "Grumpy cat staring at the camera",
            OcrText = "",
            Tags = ["kot"],
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
