using System.Net;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeIndexingJobTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildDiscordId = 1UL;
    private const ulong ChannelDiscordId = 2UL;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _channel = null!;
    private UserEntity _author = null!;
    private FakeMemeHttpHandler _http = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.MemeIndex.ExecuteDeleteAsync();
        await _db.BackfillCheckpoints.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = GuildDiscordId, Name = "g" };
        _db.Guilds.Add(_guild);
        await _db.SaveChangesAsync();

        _channel = new ChannelEntity { DiscordId = ChannelDiscordId, GuildId = _guild.Id, Name = "memes", Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 3UL, Username = "u" };
        _db.Channels.Add(_channel);
        _db.Users.Add(_author);
        await _db.SaveChangesAsync();

        _http = new FakeMemeHttpHandler();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ExecuteAsync_FullRun_IndexesEveryImageAttachment()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        AddMessage(1002UL, Attachment(13UL, "c.png"), Attachment(14UL, "clip.mp4"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));

        await RunJobAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.Equal([11UL, 12UL, 13UL], rows.Select(r => r.AttachmentDiscordId));
        Assert.All(rows, r =>
        {
            Assert.Equal(MemeIndexStatus.Indexed, r.Status);
            Assert.Equal(GuildDiscordId, r.GuildDiscordId);
            Assert.Equal(ChannelDiscordId, r.ChannelDiscordId);
            Assert.Equal("test/model", r.ModelId);
            Assert.NotNull(r.ContentHash);
            Assert.NotNull(r.IndexedAtUtc);
            Assert.NotEqual(Guid.Empty, r.MessageId);
        });
        Assert.Equal(3, _http.ModelCalls);

        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(BackfillStatus.Completed, checkpoint.Status);
        Assert.Equal(3, checkpoint.ProcessedCount);
    }

    [Fact]
    public async Task ExecuteAsync_RerunOverTerminalRows_MakesNoModelCalls()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();
        Assert.Equal(1, _http.ModelCalls);

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        Assert.Equal(1, await verify.MemeIndex.CountAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SameBytesTwice_DedupesWithoutSecondModelCall()
    {
        AddMessage(1001UL, Attachment(11UL, "original.png"));
        AddMessage(1002UL, Attachment(12UL, "repost.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(7));
        _http.SetImage(12UL, Png(7));

        await RunJobAsync();

        Assert.Equal(1, _http.ModelCalls);
        await using var verify = NewContext();
        var rows = await verify.MemeIndex.OrderBy(m => m.AttachmentDiscordId).ToListAsync();
        Assert.All(rows, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));
        Assert.Equal(rows[0].ContentHash, rows[1].ContentHash);
        Assert.Equal(rows[0].DescriptionPl, rows[1].DescriptionPl);
        Assert.NotNull(rows[0].RawResponseJson);
        Assert.Null(rows[1].RawResponseJson);
    }

    [Fact]
    public async Task ExecuteAsync_OutcomeMapping_SkippedVsFailed()
    {
        AddMessage(1001UL, Attachment(11UL, "refused.png"));
        AddMessage(1002UL, Attachment(12UL, "flaky.png"));
        AddMessage(1003UL, Attachment(13UL, "not-an-image.png"));
        AddMessage(1004UL, Attachment(14UL, "dead.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Encoding.ASCII.GetBytes("definitely not image bytes"));
        _http.DeadAttachments.Add(14UL);
        _http.RefusalFor.Add(Png(1));
        _http.TransientErrorFor.Add(Png(2));

        await RunJobAsync();

        await using var verify = NewContext();
        var byId = await verify.MemeIndex.ToDictionaryAsync(m => m.AttachmentDiscordId);
        Assert.Equal(MemeIndexStatus.Skipped, byId[11UL].Status);
        Assert.StartsWith("model refusal", byId[11UL].Error);
        Assert.Equal(MemeIndexStatus.Failed, byId[12UL].Status);
        Assert.Equal(1, byId[12UL].AttemptCount);
        Assert.Equal(MemeIndexStatus.Skipped, byId[13UL].Status);
        Assert.StartsWith("unsupported", byId[13UL].Error);
        Assert.Equal(MemeIndexStatus.Skipped, byId[14UL].Status);
        Assert.StartsWith("dead attachment", byId[14UL].Error);

        // Re-run retries ONLY the Failed row — and this time it succeeds.
        _http.TransientErrorFor.Clear();
        var callsBefore = _http.ModelCalls;
        await RunJobAsync();

        Assert.Equal(callsBefore + 1, _http.ModelCalls);
        await using var verify2 = NewContext();
        var retried = await verify2.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Indexed, retried.Status);
        Assert.Equal(2, retried.AttemptCount);
        Assert.Null(retried.Error);
    }

    [Fact]
    public async Task ExecuteAsync_StrandedPendingRow_IsReprocessed()
    {
        // A mid-attachment interruption leaves a committed Pending row behind
        // (the executor's failure path flushes it). It must not be terminal.
        AddMessage(1001UL, Attachment(11UL, "stranded.png"));
        await _db.SaveChangesAsync();
        _db.MemeIndex.Add(new MemeIndexEntity
        {
            MessageId = _db.Messages.Single(m => m.DiscordId == 1001UL).Id,
            GuildDiscordId = GuildDiscordId,
            ChannelDiscordId = ChannelDiscordId,
            MessageDiscordId = 1001UL,
            AttachmentDiscordId = 11UL,
            FileName = "stranded.png",
            FileSizeBytes = 123,
            Status = MemeIndexStatus.Pending
        });
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
        Assert.Equal(1, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_InterruptedRun_ResumesFromMessageCursor()
    {
        AddMessage(1001UL, Attachment(11UL, "done-before-crash.png"));
        AddMessage(1002UL, Attachment(12UL, "after-cursor.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        // Simulate a SIGKILLed run: checkpoint left InProgress with the cursor
        // past message 1001 — the executor only honors the cursor in this state.
        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            LastProcessedId = 1001UL,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await RunJobAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(12UL, row.AttachmentDiscordId);
        Assert.Equal(1, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_MaxImagesPerRun_CapsTheRunAndNextRunContinues()
    {
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        AddMessage(1002UL, Attachment(12UL, "b.png"));
        AddMessage(1003UL, Attachment(13UL, "c.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.SetImage(13UL, Png(3));

        await RunJobAsync(maxImagesPerRun: 2);

        await using var verify = NewContext();
        Assert.Equal(2, await verify.MemeIndex.CountAsync());
        Assert.Equal(2, _http.ModelCalls);

        await RunJobAsync(maxImagesPerRun: 2);

        await using var verify2 = NewContext();
        Assert.Equal(3, await verify2.MemeIndex.CountAsync());
        Assert.Equal(3, _http.ModelCalls);
    }

    private async Task RunJobAsync(int maxImagesPerRun = 500)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention());
        services.Configure<MemeIndexOptions>(o =>
        {
            o.ChannelIds = [ChannelDiscordId];
            o.MaxImagesPerRun = maxImagesPerRun;
        });
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = "test/model";
            o.RequestDelayMs = 0;
        });
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_http));
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeAttachmentIndexer>();
        services.AddScoped<BackfillJobExecutor>();
        services.AddScoped<MemeIndexingJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemeIndexingJob>()
            .ExecuteAsync(GuildDiscordId, CancellationToken.None);
    }

    private void AddMessage(ulong discordId, params string[] attachments)
    {
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = _channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = $"[{string.Join(",", attachments)}]",
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    // The 4-field PascalCase shape MessageEventHandler/MessagesBackfillJob serialize.
    private static string Attachment(ulong id, string fileName) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{ChannelDiscordId}/{id}/{fileName}?ex=expired\",\"FileName\":\"{fileName}\",\"FileSize\":123}}";

    // Distinct valid-PNG-magic payloads (≥12 bytes for the sniffer).
    private static byte[] Png(byte seed) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, seed, seed, seed];

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}

internal sealed class FakeMemeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<ulong, byte[]> _imagesByAttachment = [];

    public HashSet<ulong> DeadAttachments { get; } = [];
    public List<byte[]> RefusalFor { get; } = [];
    public List<byte[]> TransientErrorFor { get; } = [];
    public int ModelCalls { get; private set; }

    public void SetImage(ulong attachmentId, byte[] bytes) => _imagesByAttachment[attachmentId] = bytes;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("attachments/refresh-urls", StringComparison.Ordinal))
            return await HandleRefreshAsync(request, cancellationToken);

        if (path.EndsWith("chat/completions", StringComparison.Ordinal))
            return await HandleModelAsync(request, cancellationToken);

        return HandleCdn(request);
    }

    private async Task<HttpResponseMessage> HandleRefreshAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var urls = JsonDocument.Parse(body).RootElement.GetProperty("attachment_urls");

        var refreshed = urls.EnumerateArray()
            .Select(u => u.GetString()!)
            .Where(u => !DeadAttachments.Contains(AttachmentIdOf(u)))
            .Select(u => new { original = u, refreshed = u + "?sig=fresh" })
            .ToList();

        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { refreshed_urls = refreshed }));
    }

    private HttpResponseMessage HandleCdn(HttpRequestMessage request)
    {
        var id = AttachmentIdOf(request.RequestUri!.AbsoluteUri);
        if (!_imagesByAttachment.TryGetValue(id, out var bytes))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }

    private async Task<HttpResponseMessage> HandleModelAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var imageBytes = ExtractImageBytes(body);
        ModelCalls++;

        if (TransientErrorFor.Any(b => b.AsSpan().SequenceEqual(imageBytes)))
            return Json(HttpStatusCode.InternalServerError, """{"error":"upstream exploded"}""");

        if (RefusalFor.Any(b => b.AsSpan().SequenceEqual(imageBytes)))
            return Json(HttpStatusCode.OK,
                """{"choices":[{"message":{"content":null,"refusal":"safety"},"finish_reason":"stop"}]}""");

        var metadata = JsonSerializer.Serialize(new
        {
            description_pl = $"Opis obrazka {imageBytes[^1]}",
            description_en = $"Description of image {imageBytes[^1]}",
            ocr_text = "",
            tags = new[] { "test", "mem" },
            source = (string?)null,
            template = (string?)null
        });
        var response = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = metadata, refusal = (string?)null }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 100, completion_tokens = 50, cost = 0.0016m }
        });
        return Json(HttpStatusCode.OK, response);
    }

    private static byte[] ExtractImageBytes(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var dataUrl = doc.RootElement.GetProperty("messages")[1].GetProperty("content")[0]
            .GetProperty("image_url").GetProperty("url").GetString()!;
        return Convert.FromBase64String(dataUrl[(dataUrl.IndexOf("base64,", StringComparison.Ordinal) + 7)..]);
    }

    // .../attachments/{channelId}/{attachmentId}/{fileName}
    private static ulong AttachmentIdOf(string url)
    {
        var segments = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return ulong.Parse(segments[^2]);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = name switch
            {
                AttachmentUrlRefreshService.HttpClientName => new Uri("https://discord.test/api/v10/"),
                OpenRouterClient.HttpClientName => new Uri("https://openrouter.test/api/v1/"),
                _ => null
            }
        };
}
