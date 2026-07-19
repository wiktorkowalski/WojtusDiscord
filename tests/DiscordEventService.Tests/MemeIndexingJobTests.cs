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
    // Mirrors the MemeIndexOptions.MaxImageBytes default.
    private const int DefaultMaxImageBytes = 25 * 1024 * 1024;

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
        // Transient model errors refund the attempt (#293): only deterministic failures
        // may walk a row toward the sweep's permanent-abandonment cap.
        Assert.Equal(0, byId[12UL].AttemptCount);
        Assert.StartsWith("transient", byId[12UL].Error);
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
        Assert.Equal(1, retried.AttemptCount);
        Assert.Null(retried.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshBatchFailure_MarksFailedAndLaterRunHeals()
    {
        // A transient refresh-urls failure (5xx/timeout) must not mark the
        // batch Skipped — Skipped is terminal and the memes would be silently
        // lost from search forever. It also must not burn a retry attempt:
        // the attachment itself was never actually processed.
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));
        _http.RefreshFailuresRemaining = 1;

        await RunJobAsync();

        await using var verify = NewContext();
        var rows = await verify.MemeIndex.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(MemeIndexStatus.Failed, r.Status);
            Assert.Equal(0, r.AttemptCount);
        });
        Assert.Equal(0, _http.ModelCalls);

        // Next run: refresh succeeds, both rows index normally.
        await RunJobAsync();

        await using var verify2 = NewContext();
        var healed = await verify2.MemeIndex.ToListAsync();
        Assert.All(healed, r => Assert.Equal(MemeIndexStatus.Indexed, r.Status));
        Assert.Equal(2, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteSweepAsync_RefreshBatchFailures_NeverExhaustAttemptCap()
    {
        // The sweep abandons rows at SweepMaxFailedAttempts — repeated batch
        // failures must stay below it so a flaky week can't orphan a meme.
        AddMessage(1001UL, Attachment(11UL, "a.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        for (var i = 0; i < MemeIndexingJob.SweepMaxFailedAttempts + 1; i++)
        {
            _http.RefreshFailuresRemaining = 1;
            await RunSweepAsync();
        }

        await RunSweepAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Indexed, row.Status);
    }

    [Fact]
    public async Task ExecuteAsync_UrlDeclinedByDiscord_StaysSkipped()
    {
        // Contrast case: a 2xx refresh response that omits the URL means the
        // attachment is genuinely gone — terminal Skip remains correct.
        AddMessage(1001UL, Attachment(11UL, "deleted.png"));
        await _db.SaveChangesAsync();
        _http.DeadAttachments.Add(11UL);

        await RunJobAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Skipped, row.Status);
        Assert.StartsWith("dead attachment", row.Error);
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

    [Fact]
    public async Task ExecuteAsync_MetadataOversizedAttachment_SkippedWithoutDownload()
    {
        // Discord already told us the file size — an attachment over MaxImageBytes
        // must be pre-skipped from metadata, not buffered in full first.
        AddMessage(1001UL, Attachment(11UL, "huge.png", fileSize: 26L * 1024 * 1024));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));

        await RunJobAsync();

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Skipped, row.Status);
        Assert.StartsWith("unsupported: image too large", row.Error);
        Assert.Equal(0, _http.CdnRequests);
        Assert.Equal(0, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_DownloadExceedsBufferCap_SkippedNotRetriedForever()
    {
        // Metadata can lie small. The download client's buffer cap is the backstop —
        // and blowing it is deterministic, so it must be a terminal Skip, not a
        // transient Failure that every future sweep retries (and refunds) forever.
        AddMessage(1001UL, Attachment(11UL, "liar.png", fileSize: 10));
        await _db.SaveChangesAsync();
        var oversized = new byte[100];
        Png(1).CopyTo(oversized, 0);
        _http.SetImage(11UL, oversized);

        await RunJobAsync(maxImageBytes: 64);

        await using var verify = NewContext();
        var row = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 11UL);
        Assert.Equal(MemeIndexStatus.Skipped, row.Status);
        Assert.StartsWith("unsupported: image too large", row.Error);
        Assert.Equal(0, _http.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_CapSplitsMultiAttachmentMessage_ResumeDoesNotSkipSiblings()
    {
        // MaxImagesPerRun can cut a multi-attachment message in half. The resume
        // cursor must NOT advance to that message: a crash before the run's
        // Completed flip would otherwise make the resumed run skip the siblings.
        AddMessage(1001UL, Attachment(11UL, "a.png"), Attachment(12UL, "b.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(11UL, Png(1));
        _http.SetImage(12UL, Png(2));

        await RunJobAsync(maxImagesPerRun: 1);

        // Simulate a SIGKILL between the last per-item save and MarkCompleted:
        // status stays InProgress, so the next run honors the saved cursor.
        await using (var crash = NewContext())
        {
            await crash.BackfillCheckpoints
                .Where(c => c.Type == BackfillType.MemeIndex)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, BackfillStatus.InProgress));
        }

        await RunJobAsync();

        await using var verify = NewContext();
        var sibling = await verify.MemeIndex.SingleAsync(m => m.AttachmentDiscordId == 12UL);
        Assert.Equal(MemeIndexStatus.Indexed, sibling.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MidFlightResume_NeverReportsProcessedAboveTotal()
    {
        // A resumed run keeps accumulating ProcessedCount — TotalCount must
        // include that prior progress or the status endpoint shows processed > total.
        AddMessage(1001UL, Attachment(11UL, "done-before-crash.png"));
        AddMessage(1002UL, Attachment(12UL, "after-cursor.png"));
        await _db.SaveChangesAsync();
        _http.SetImage(12UL, Png(2));

        _db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildDiscordId,
            Type = BackfillType.MemeIndex,
            Status = BackfillStatus.InProgress,
            LastProcessedId = 1001UL,
            ProcessedCount = 5,
            TotalCount = 6,
            StartedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await RunJobAsync();

        await using var verify = NewContext();
        var checkpoint = await verify.BackfillCheckpoints.SingleAsync(c => c.Type == BackfillType.MemeIndex);
        Assert.Equal(6, checkpoint.ProcessedCount);
        Assert.Equal(6, checkpoint.TotalCount);
    }

    private Task RunJobAsync(int maxImagesPerRun = 500, int maxImageBytes = DefaultMaxImageBytes)
        => RunAsync(maxImagesPerRun, maxImageBytes, sweep: false);

    private Task RunSweepAsync() => RunAsync(maxImagesPerRun: 500, DefaultMaxImageBytes, sweep: true);

    private async Task RunAsync(int maxImagesPerRun, int maxImageBytes, bool sweep)
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
            o.MaxImageBytes = maxImageBytes;
        });
        services.Configure<OpenRouterOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.Model = "test/model";
            o.RequestDelayMs = 0;
        });
        services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(_http, maxImageBytes));
        services.AddScoped<MemeSampleService>();
        services.AddScoped<AttachmentUrlRefreshService>();
        services.AddScoped<OpenRouterClient>();
        services.AddScoped<MemeAttachmentIndexer>();
        services.AddScoped<BackfillJobExecutor>();
        services.AddScoped<MemeIndexingJob>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<MemeIndexingJob>();
        if (sweep)
            await job.ExecuteSweepAsync(GuildDiscordId, CancellationToken.None);
        else
            await job.ExecuteAsync(GuildDiscordId, CancellationToken.None);
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
    private static string Attachment(ulong id, string fileName, long fileSize = 123) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.test/attachments/{ChannelDiscordId}/{id}/{fileName}?ex=expired\",\"FileName\":\"{fileName}\",\"FileSize\":{fileSize}}}";

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
    public int RefreshFailuresRemaining { get; set; }
    public int ModelCalls { get; private set; }
    public int CdnRequests { get; private set; }

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
        if (RefreshFailuresRemaining > 0)
        {
            RefreshFailuresRemaining--;
            return Json(HttpStatusCode.InternalServerError, """{"message":"upstream exploded"}""");
        }

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
        CdnRequests++;
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

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler, long maxImageBytes = 25 * 1024 * 1024) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = name switch
            {
                AttachmentUrlRefreshService.HttpClientName => new Uri("https://discord.test/api/v10/"),
                OpenRouterClient.HttpClientName => new Uri("https://openrouter.test/api/v1/"),
                _ => null
            }
        };
        // Mirrors the prod discord-cdn client's download hard cap.
        if (name == MemeBenchmarkJob.DownloadHttpClientName)
            client.MaxResponseContentBufferSize = maxImageBytes;
        return client;
    }
}
