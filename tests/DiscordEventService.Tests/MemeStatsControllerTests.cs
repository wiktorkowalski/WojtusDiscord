using System.Net;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscordEventService.Tests;

// #395: MemeStatsController over HTTP — the real JSON pipeline, the input checks, and the
// thumbnail redirect with its rules and caps. Discord is a stub; the database is real.
public sealed class MemeStatsControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string BasePath = "/api/stats/memes";
    private const ulong AttachmentId = 910000000000000001UL;
    private const string FreshSignature = "?ex=fresh&hm=sig";

    private DiscordDbContext _db = null!;
    private MemeStatsTestData _data = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        _data = new MemeStatsTestData(_db);
        await _data.ResetAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ───────────────────────────── JSON ─────────────────────────────

    [Fact]
    public async Task Index_ThroughTheJsonPipeline_WritesSnowflakesAsStrings()
    {
        await _data.AddIndexedAsync(AttachmentId, a => a.ImageKind = MemeImageKind.Comic);
        await using var host = await StartAsync();

        using var json = await GetJsonAsync(host, BasePath);

        var root = json.RootElement;
        var channel = root.GetProperty("channels")[0];
        Assert.Equal(JsonValueKind.String, channel.GetProperty("channelDiscordId").ValueKind);
        Assert.Equal(MemeStatsTestData.ChannelDiscordId.ToString(), channel.GetProperty("channelDiscordId").GetString());
        Assert.False(root.GetProperty("automaticIndexing").GetBoolean());
        Assert.Equal(1, root.GetProperty("status").GetProperty("indexed").GetInt64());
        Assert.Equal(0, root.GetProperty("notIndexed").GetProperty("count").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("notIndexed").GetProperty("oldestPostedAtUtc").ValueKind);
        var imageKind = root.GetProperty("distributions").GetProperty("imageKind");
        Assert.Equal("comic", imageKind.GetProperty("buckets")[0].GetProperty("name").GetString());
        Assert.Equal(0, imageKind.GetProperty("missingCount").GetInt64());
    }

    [Fact]
    public async Task Search_ThroughTheJsonPipeline_WritesSnowflakesAsStringsAndNoCdnUrl()
    {
        var meme = await _data.AddIndexedAsync(AttachmentId, a => a.Tags = ["rakieta"]);
        await using var host = await StartAsync();

        var body = await host.Client.GetStringAsync($"{BasePath}/search?q=rakieta");

        using var json = JsonDocument.Parse(body);
        var hit = Assert.Single(json.RootElement.GetProperty("hits").EnumerateArray());
        Assert.Equal(AttachmentId.ToString(), hit.GetProperty("attachmentDiscordId").GetString());
        Assert.Equal(meme.MessageDiscordId.ToString(), hit.GetProperty("messageDiscordId").GetString());
        Assert.Equal(MemeStatsTestData.ChannelDiscordId.ToString(), hit.GetProperty("channelDiscordId").GetString());
        Assert.Equal($"{BasePath}/thumbnails/{AttachmentId}", hit.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(0.5, json.RootElement.GetProperty("trigramWeight").GetDouble());
        // The image is one more request: a search calls Discord for nothing and returns no CDN URL.
        Assert.DoesNotContain("discordapp", body);
        Assert.Equal(0, host.Discord.Calls);
    }

    // meme_search_log is per-person data: the page gets the query and never who typed it or where.
    [Fact]
    public async Task SearchUsage_ThroughTheJsonPipeline_HasNoUserAndNoChannel()
    {
        const ulong UserId = 424242424242424242UL;
        const ulong DmChannelId = 777777777777777777UL;
        await _data.AddIndexedAsync(AttachmentId);
        await _data.AddSearchAsync(
            MemeSearchSource.SlashCommand, DateTime.UtcNow.AddMinutes(-1), "rakieta", durationMs: 12,
            userDiscordId: UserId, channelDiscordId: DmChannelId, hits: AttachmentId);
        await using var host = await StartAsync();

        var body = await host.Client.GetStringAsync($"{BasePath}/search-usage");

        Assert.DoesNotContain(UserId.ToString(), body);
        Assert.DoesNotContain(DmChannelId.ToString(), body);
        using var json = JsonDocument.Parse(body);
        Assert.DoesNotContain(PropertyNames(json.RootElement), name =>
            name.Contains("user", StringComparison.OrdinalIgnoreCase)
            || name.Contains("channel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("guild", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(30, json.RootElement.GetProperty("days").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("searchCount").GetInt64());
        var search = Assert.Single(json.RootElement.GetProperty("latest").EnumerateArray());
        Assert.Equal(
            ["durationMs", "query", "resultCount", "searchedAtUtc", "source", "topHit"],
            search.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("rakieta", search.GetProperty("query").GetString());
        Assert.Equal(AttachmentId.ToString(), search.GetProperty("topHit").GetProperty("attachmentDiscordId").GetString());
    }

    // ───────────────────────────── Input checks ─────────────────────────────

    [Theory]
    [InlineData("/search")]
    [InlineData("/search?q=")]
    [InlineData("/search?q=%20%20")]
    [InlineData("/search?q=a&limit=0")]
    [InlineData("/search?q=a&limit=21")]
    [InlineData("/search-usage?days=0")]
    [InlineData("/search-usage?days=366")]
    public async Task Request_InputOutOfBounds_Returns400WithTheErrorShape(string path)
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(BasePath + path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["error"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task Search_QueryOverTheLengthLimit_Returns400AndAtTheLimit200()
    {
        await using var host = await StartAsync();

        var tooLong = await host.Client.GetAsync($"{BasePath}/search?q={new string('a', MemeStatsController.MaxQueryLength + 1)}");
        var atLimit = await host.Client.GetAsync($"{BasePath}/search?q={new string('a', MemeStatsController.MaxQueryLength)}");

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
    }

    [Fact]
    public async Task Search_EverySearchSlotTaken_Returns429WithTheErrorShape()
    {
        await using var host = await StartAsync();
        for (var i = 0; i < MemeDashboardLimits.MaxConcurrentSearches; i++)
            await host.Limits.SearchGate.WaitAsync();

        var response = await host.Client.GetAsync($"{BasePath}/search?q=rakieta");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["error"], json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    // ───────────────────────────── Thumbnails ─────────────────────────────

    [Fact]
    public async Task Thumbnail_IndexedMeme_RedirectsToTheUrlDiscordSigned()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(MemeStatsTestData.StoredUrlOf(AttachmentId) + FreshSignature, response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(MemeThumbnailResolver.BrowserMaxAge, response.Headers.CacheControl.MaxAge);
        // Discord got the stored URL of this attachment, without its expired signature.
        Assert.Equal([MemeStatsTestData.StoredUrlOf(AttachmentId)], Assert.Single(host.Discord.RequestedUrls));
    }

    [Fact]
    public async Task Thumbnail_SecondRequestForTheSameImage_MakesNoSecondCallToDiscord()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync();

        var first = await host.Client.GetAsync(ThumbnailPath(AttachmentId));
        var second = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(1, host.Discord.Calls);
    }

    [Fact]
    public async Task Thumbnail_ManyRequestsForTheSameImageAtOnce_MakeOneCallToDiscord()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => host.Client.GetAsync(ThumbnailPath(AttachmentId))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        Assert.Equal(1, host.Discord.Calls);
    }

    [Fact]
    public async Task Thumbnail_IdWithNoIndexRow_Returns404WithoutACallToDiscord()
    {
        // The image exists in the meme channel, but nothing indexed it.
        await _data.AddImageMessageAsync(AttachmentId);
        await using var host = await StartAsync();

        await AssertNotServedAsync(host);
    }

    // The channel left MemeIndex:ChannelIds after the meme was indexed: no longer served.
    [Fact]
    public async Task Thumbnail_ChannelNoLongerAMemeChannel_Returns404WithoutACallToDiscord()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync(channelIds: [12345UL]);

        await AssertNotServedAsync(host);
    }

    [Fact]
    public async Task Thumbnail_DeletedMessage_Returns404WithoutACallToDiscord()
    {
        var meme = await _data.AddMemeAsync(AttachmentId, messageDeleted: true);
        await _data.AddAnnotationAsync(meme);
        await using var host = await StartAsync();

        await AssertNotServedAsync(host);
    }

    [Theory]
    [InlineData(MemeIndexStatus.Pending)]
    [InlineData(MemeIndexStatus.Failed)]
    [InlineData(MemeIndexStatus.Skipped)]
    public async Task Thumbnail_RowThatIsNotIndexed_Returns404WithoutACallToDiscord(MemeIndexStatus status)
    {
        await _data.AddMemeAsync(AttachmentId, status);
        await using var host = await StartAsync();

        await AssertNotServedAsync(host);
    }

    [Fact]
    public async Task Thumbnail_MessageNoLongerHoldsTheAttachment_Returns404WithoutACallToDiscord()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await SetAttachmentsJsonAsync("[]");
        await using var host = await StartAsync();

        await AssertNotServedAsync(host);
    }

    // The stored URL is data too: only a Discord CDN URL is ever sent on.
    [Theory]
    [InlineData("https://evil.test/attachments/1/2/meme.png")]
    [InlineData("http://cdn.discordapp.com/attachments/1/2/meme.png")]
    [InlineData("https://cdn.discordapp.com.evil.test/attachments/1/2/meme.png")]
    public async Task Thumbnail_StoredUrlThatIsNotAnHttpsDiscordCdnUrl_Returns404WithoutACallToDiscord(string storedUrl)
    {
        await _data.AddIndexedAsync(AttachmentId);
        await SetAttachmentsJsonAsync(
            $"[{{\"Id\":{AttachmentId},\"Url\":\"{storedUrl}\",\"FileName\":\"meme.png\",\"FileSize\":123}}]");
        await using var host = await StartAsync();

        await AssertNotServedAsync(host);
    }

    // No open redirect: whatever the refresh call answers, the browser goes to the Discord CDN only.
    [Fact]
    public async Task Thumbnail_RefreshedUrlOnAnotherHost_Returns404()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync(urls => urls.Select(u => (u, (string?)"https://evil.test/meme.png")));

        var response = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Thumbnail_DiscordDeclinesTheUrl_Returns404AndDoesNotAskAgain()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync(_ => []);

        var first = await host.Client.GetAsync(ThumbnailPath(AttachmentId));
        var second = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(1, host.Discord.Calls);
    }

    // A failed call says nothing about the image: 503, not 404. And Discord is not asked
    // again at once for the same image.
    [Fact]
    public async Task Thumbnail_DiscordFails_Returns503AndDoesNotAskAgainAtOnce()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync(failWith: HttpStatusCode.InternalServerError);

        var first = await host.Client.GetAsync(ThumbnailPath(AttachmentId));
        var second = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(1, host.Discord.Calls);
    }

    [Fact]
    public async Task Thumbnail_RefreshBudgetOfTheMinuteUsedUp_Returns503WithoutACallToDiscord()
    {
        await _data.AddIndexedAsync(AttachmentId);
        await using var host = await StartAsync();
        using var allPermits = host.Limits.RefreshBudget.AttemptAcquire(MemeDashboardLimits.MaxRefreshesPerMinute);

        var response = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.True(allPermits.IsAcquired);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, host.Discord.Calls);
    }

    // A signed URL that expires inside the margin is served and not kept: the next request asks again.
    [Fact]
    public async Task Thumbnail_SignedUrlAboutToExpire_IsNotCached()
    {
        await _data.AddIndexedAsync(AttachmentId);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString("x");
        await using var host = await StartAsync(urls => urls.Select(u => (u, (string?)$"{u}?ex={expiry}&hm=sig")));

        var first = await host.Client.GetAsync(ThumbnailPath(AttachmentId));
        await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(2, host.Discord.Calls);
    }

    private static string ThumbnailPath(ulong attachmentDiscordId) => $"{BasePath}/thumbnails/{attachmentDiscordId}";

    // Not servable: the database says so, and Discord is not asked.
    private static async Task AssertNotServedAsync(TestHost host)
    {
        var response = await host.Client.GetAsync(ThumbnailPath(AttachmentId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, host.Discord.Calls);
    }

    private Task<int> SetAttachmentsJsonAsync(string attachmentsJson) =>
        _db.Messages
            .Where(m => m.DiscordId == MemeStatsTestData.MessageIdOf(AttachmentId))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AttachmentsJson, attachmentsJson));

    private static async Task<JsonDocument> GetJsonAsync(TestHost host, string path) =>
        JsonDocument.Parse(await host.Client.GetStringAsync(path));

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    // The production wiring of the meme page over this class's database, with Discord stubbed.
    // refresh: what Discord answers for the URLs it is asked to sign (default: signs each one).
    private async Task<TestHost> StartAsync(
        Func<IReadOnlyList<string>, IEnumerable<(string Original, string? Refreshed)>>? refresh = null,
        HttpStatusCode? failWith = null,
        ulong[]? channelIds = null)
    {
        var discord = new StubDiscordApi(refresh ?? (urls => urls.Select(u => (u, (string?)(u + FreshSignature)))), failWith);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(MemeStatsController).Assembly)
            .AddJsonOptions(o => DashboardJson.Configure(o.JsonSerializerOptions));
        builder.Services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        builder.Services.Configure<MemeIndexOptions>(o => o.ChannelIds = channelIds ?? [MemeStatsTestData.ChannelDiscordId]);
        builder.Services.Configure<DiscordOptions>(o => o.Token = new string('x', 60));
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(discord));
        builder.Services.AddSingleton<MemeSearchLogWriter>();
        builder.Services.AddSingleton<MemeDashboardLimits>();
        builder.Services.AddScoped<MemeSearchService>();
        builder.Services.AddScoped<MemeSampleService>();
        builder.Services.AddScoped<IMemeIndexSummaryReader, MemeIndexSummaryReader>();
        builder.Services.AddScoped<AttachmentUrlRefreshService>();
        builder.Services.AddScoped<IMemeStatsReader, MemeStatsReader>();
        builder.Services.AddScoped<IMemeThumbnailResolver, MemeThumbnailResolver>();

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        // app.Urls still holds port 0; the server knows the port it was given.
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        return new TestHost(app, client, discord);
    }

    private sealed class TestHost(WebApplication app, HttpClient client, StubDiscordApi discord) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public StubDiscordApi Discord => discord;
        public MemeDashboardLimits Limits => app.Services.GetRequiredService<MemeDashboardLimits>();

        public async ValueTask DisposeAsync()
        {
            // A tester search writes its log row after the answer: let it finish before the database goes.
            await app.Services.GetRequiredService<MemeSearchLogWriter>().LastWrite;
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // POST attachments/refresh-urls, the only call the meme page makes to Discord.
    private sealed class StubDiscordApi(
        Func<IReadOnlyList<string>, IEnumerable<(string Original, string? Refreshed)>> refresh,
        HttpStatusCode? failWith) : HttpMessageHandler
    {
        private readonly List<List<string>> _requestedUrls = [];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<List<string>> RequestedUrls
        {
            get
            {
                lock (_requestedUrls)
                    return [.. _requestedUrls];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Assert.Equal("https://discord.test/api/v10/attachments/refresh-urls", request.RequestUri!.ToString());

            var urls = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken))
                .GetProperty("attachment_urls").EnumerateArray().Select(e => e.GetString()!).ToList();
            lock (_requestedUrls)
                _requestedUrls.Add(urls);

            if (failWith is { } status)
                return new HttpResponseMessage(status) { Content = new StringContent("{\"message\":\"boom\"}") };

            var body = JsonSerializer.Serialize(new
            {
                refreshed_urls = refresh(urls).Select(r => new { original = r.Original, refreshed = r.Refreshed }),
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
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
