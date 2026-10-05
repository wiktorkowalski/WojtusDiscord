using System.Net;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Endpoints;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// For the Overview tests that are not about the meme index.
internal sealed class FixedMemeIndexSummaryReader(long indexed = 0, long waiting = 0) : IMemeIndexSummaryReader
{
    public Task<MemeIndexSummary> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MemeIndexSummary(indexed, waiting));
}

// #397: how many meme images wait for an annotation, in the service, on the ops status endpoint
// and on the dashboard Overview.
public sealed class MemeIndexWaitingCountTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong MemeChannelDiscordId = 10UL;
    private const ulong OtherChannelDiscordId = 20UL;
    private const int MaxImageBytes = 1000;
    private const string StatusPath = "/api/ops/meme-index/status";

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _memeChannel = null!;
    private ChannelEntity _otherChannel = null!;
    private UserEntity _author = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeAnnotations.ExecuteDeleteAsync();
        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.BackfillCheckpoints.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        _db.Guilds.Add(_guild);
        await _db.SaveChangesAsync();

        _memeChannel = new ChannelEntity { DiscordId = MemeChannelDiscordId, GuildId = _guild.Id, Name = "memes", Type = ChannelType.Text };
        _otherChannel = new ChannelEntity { DiscordId = OtherChannelDiscordId, GuildId = _guild.Id, Name = "general", Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 5UL, Username = "u" };
        _db.AddRange(_memeChannel, _otherChannel, _author);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task CountWaitingAsync_ImageWithNoRow_Counts()
    {
        AddMessage(_memeChannel, 100UL, Attachment(1000UL, "new.png"));
        await _db.SaveChangesAsync();

        Assert.Equal(1, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    // Any row means someone already handled the attachment: Indexed, and a terminal Skipped too.
    [Theory]
    [InlineData(MemeIndexStatus.Indexed)]
    [InlineData(MemeIndexStatus.Skipped)]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Pending)]
    public async Task CountWaitingAsync_ImageWithARow_DoesNotCount(MemeIndexStatus status)
    {
        var message = AddMessage(_memeChannel, 100UL, Attachment(1000UL, "done.png"));
        await _db.SaveChangesAsync();
        await AddRowAsync(message, 1000UL, "done.png", status);

        Assert.Equal(0, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("anim.gif")]
    [InlineData("ANIM.GIF")]
    public async Task CountWaitingAsync_Gif_DoesNotCount(string fileName)
    {
        AddMessage(_memeChannel, 100UL, Attachment(1000UL, fileName));
        await _db.SaveChangesAsync();

        Assert.Equal(0, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CountWaitingAsync_ImageOfADeletedMessage_DoesNotCount()
    {
        AddMessage(_memeChannel, 100UL, Attachment(1000UL, "gone.png"), isDeleted: true);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CountWaitingAsync_ImageOutsideTheMemeChannels_DoesNotCount()
    {
        AddMessage(_otherChannel, 100UL, Attachment(1000UL, "chat.png"));
        await _db.SaveChangesAsync();

        Assert.Equal(0, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    // The indexer skips on the same rule (MemeIndexOptions.ExceedsMaxImageBytes): the limit itself passes.
    [Theory]
    [InlineData(MaxImageBytes + 1, 0)]
    [InlineData(MaxImageBytes, 1)]
    public async Task CountWaitingAsync_ImageAtTheSizeLimit_CountsOnlyWhenNotOverIt(int fileSize, int expected)
    {
        AddMessage(_memeChannel, 100UL, Attachment(1000UL, "big.png", fileSize));
        await _db.SaveChangesAsync();

        Assert.Equal(expected, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CountWaitingAsync_FileThatIsNotAnImage_DoesNotCount()
    {
        AddMessage(_memeChannel, 100UL, Attachment(1000UL, "clip.mp4"));
        await _db.SaveChangesAsync();

        Assert.Equal(0, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CountWaitingAsync_NoMemeChannelConfigured_IsZero()
    {
        AddMessage(_memeChannel, 100UL, Attachment(1000UL, "new.png"));
        await _db.SaveChangesAsync();

        Assert.Equal(0, await NewSampleService(channelIds: []).CountWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CountWaitingAsync_MixedCorpus_CountsOnlyTheImagesWithNoRow()
    {
        await SeedMixedCorpusAsync();

        Assert.Equal(2, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    // The existing names are what the runbooks' jq lines read: the new field is additive.
    [Fact]
    public async Task Status_MixedCorpus_ReturnsTheWaitingCountNextToTheUnchangedFields()
    {
        await SeedMixedCorpusAsync();
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(StatusPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(["annotations", "checkpoints", "rows", "waiting"], root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(2, root.GetProperty("waiting").GetInt32());
        var rows = root.GetProperty("rows");
        Assert.Equal(["failed", "indexed", "pending", "skipped", "total"], rows.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(1, rows.GetProperty("indexed").GetInt32());
        Assert.Equal(1, rows.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Status_NoMemeChannelConfigured_ReturnsZeroWaiting()
    {
        await SeedMixedCorpusAsync();
        await using var host = await StartAsync(o => o.ChannelIds = []);

        var response = await host.Client.GetAsync(StatusPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("waiting").GetInt32());
    }

    [Fact]
    public async Task Overview_MixedCorpus_ReturnsTheIndexedAndTheWaitingCount()
    {
        await SeedMixedCorpusAsync();
        var controller = new StatsController(_db);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var overview = (await controller.Overview(NewSummaryReader(cache), default)).Value!;

        Assert.Equal(1, overview.MemeIndexedCount);
        Assert.Equal(2, overview.MemeWaitingCount);
    }

    // The SPA reads these two names.
    [Fact]
    public async Task Overview_SerializedForTheDashboard_CarriesTheTwoFieldsInCamelCase()
    {
        await SeedMixedCorpusAsync();
        var controller = new StatsController(_db);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var overview = (await controller.Overview(NewSummaryReader(cache), default)).Value!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DashboardJson.Configure(options);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(overview, options));

        Assert.Equal(1, json.RootElement.GetProperty("memeIndexedCount").GetInt64());
        Assert.Equal(2, json.RootElement.GetProperty("memeWaitingCount").GetInt64());
    }

    // The dashboard asks on every page load: inside the cache window the candidate scan runs once.
    [Fact]
    public async Task SummaryReader_SecondReadInsideTheCacheWindow_ReturnsTheCachedNumbers()
    {
        await SeedMixedCorpusAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var first = await NewSummaryReader(cache).GetAsync(CancellationToken.None);
        AddMessage(_memeChannel, 900UL, Attachment(9000UL, "later.png"));
        await _db.SaveChangesAsync();

        var second = await NewSummaryReader(cache).GetAsync(CancellationToken.None);

        Assert.Equal(new MemeIndexSummary(Indexed: 1, Waiting: 2), first);
        Assert.Equal(first, second);
        Assert.Equal(3, await NewSampleService().CountWaitingAsync(CancellationToken.None));
    }

    // The endpoint has no auth: a second request inside the cache window must not scan again.
    [Fact]
    public async Task Status_SecondRequestInsideTheCacheWindow_ReturnsTheCachedWaitingCount()
    {
        await SeedMixedCorpusAsync();
        await using var host = await StartAsync();
        using var first = JsonDocument.Parse(await host.Client.GetStringAsync(StatusPath));
        AddMessage(_memeChannel, 900UL, Attachment(9000UL, "later.png"));
        await _db.SaveChangesAsync();

        using var second = JsonDocument.Parse(await host.Client.GetStringAsync(StatusPath));

        Assert.Equal(2, first.RootElement.GetProperty("waiting").GetInt32());
        Assert.Equal(2, second.RootElement.GetProperty("waiting").GetInt32());
    }

    // Single-flight after an expiry. Each computation makes a new summary object, so one
    // shared instance for every caller means the scan ran once. Every caller has its own
    // DbContext, like every request has.
    [Fact]
    public async Task SummaryReader_ConcurrentReadsOnAnEmptyCache_ComputeOnce()
    {
        await SeedMixedCorpusAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var contexts = Enumerable.Range(0, 8).Select(_ => NewContext()).ToList();
        try
        {
            var readers = contexts.Select(db => new MemeIndexSummaryReader(db, NewSampleService(db: db), cache)).ToList();

            var summaries = await Task.WhenAll(readers.Select(r => Task.Run(() => r.GetAsync(CancellationToken.None))));

            Assert.Equal(new MemeIndexSummary(Indexed: 1, Waiting: 2), summaries[0]);
            Assert.All(summaries, s => Assert.Same(summaries[0], s));
        }
        finally
        {
            foreach (var db in contexts)
                await db.DisposeAsync();
        }
    }

    // One indexed image, two that wait, and one of each kind that must not count.
    private async Task SeedMixedCorpusAsync()
    {
        var indexed = AddMessage(_memeChannel, 100UL, Attachment(1000UL, "indexed.png"));
        AddMessage(_memeChannel, 101UL, Attachment(1001UL, "waiting.jpg"), Attachment(1002UL, "notes.txt"));
        AddMessage(_memeChannel, 102UL, Attachment(1003UL, "waiting.webp"));
        AddMessage(_memeChannel, 103UL, Attachment(1004UL, "anim.gif"));
        AddMessage(_memeChannel, 104UL, Attachment(1005UL, "deleted.png"), isDeleted: true);
        AddMessage(_otherChannel, 105UL, Attachment(1006UL, "chat.png"));
        AddMessage(_memeChannel, 106UL, Attachment(1007UL, "big.png", MaxImageBytes + 1));
        await _db.SaveChangesAsync();
        await AddRowAsync(indexed, 1000UL, "indexed.png", MemeIndexStatus.Indexed);
    }

    private MemeSampleService NewSampleService(ulong[]? channelIds = null, DiscordDbContext? db = null) =>
        new(db ?? _db,
            Options.Create(new MemeIndexOptions { ChannelIds = channelIds ?? [MemeChannelDiscordId], MaxImageBytes = MaxImageBytes }),
            NullLogger<MemeSampleService>.Instance);

    private MemeIndexSummaryReader NewSummaryReader(IMemoryCache cache) => new(_db, NewSampleService(), cache);

    private async Task<StatusHost> StartAsync(Action<MemeIndexOptions>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        builder.Services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = [MemeChannelDiscordId];
            o.MaxImageBytes = MaxImageBytes;
            configure?.Invoke(o);
        });
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<MemeSampleService>();
        builder.Services.AddScoped<IMemeIndexSummaryReader, MemeIndexSummaryReader>();

        var app = builder.Build();
        app.MapMemeIndexEndpoints();
        await app.StartAsync();

        // app.Urls still holds port 0; the server knows the port it was given.
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new StatusHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private sealed class StatusHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private MessageEntity AddMessage(ChannelEntity channel, ulong discordId, params string[] attachments) =>
        AddMessage(channel, discordId, attachments, isDeleted: false);

    private MessageEntity AddMessage(ChannelEntity channel, ulong discordId, string attachment, bool isDeleted) =>
        AddMessage(channel, discordId, [attachment], isDeleted);

    private MessageEntity AddMessage(ChannelEntity channel, ulong discordId, string[] attachments, bool isDeleted)
    {
        var message = new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? DateTime.UtcNow : null
        };
        _db.Messages.Add(message);
        return message;
    }

    private async Task AddRowAsync(MessageEntity message, ulong attachmentId, string fileName, MemeIndexStatus status)
    {
        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = message.Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = MemeChannelDiscordId,
            MessageDiscordId = message.DiscordId,
            AttachmentDiscordId = attachmentId,
            FileName = fileName,
            FileSizeBytes = 123,
            Status = status,
            // ck_meme_index_status: a Failed or Skipped row must say why.
            Error = status is MemeIndexStatus.Failed or MemeIndexStatus.Skipped ? "test" : null,
        });
        await _db.SaveChangesAsync();
    }

    // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
    private static string Attachment(ulong id, string fileName, int fileSize = 123) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{id}/{fileName}?ex=expired\",\"FileName\":\"{fileName}\",\"FileSize\":{fileSize}}}";

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
