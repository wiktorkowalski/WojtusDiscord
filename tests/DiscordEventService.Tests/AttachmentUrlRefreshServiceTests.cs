using System.Net;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class AttachmentUrlRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_StripsQueryBatchesAndMapsResults()
    {
        List<List<string>> requests = [];
        var service = NewService(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            var sent = JsonSerializer.Deserialize<JsonElement>(body)
                .GetProperty("attachment_urls").EnumerateArray().Select(e => e.GetString()!).ToList();
            requests.Add(sent);
            var refreshed = sent.Select(u => new { original = u, refreshed = u + "?ex=fresh" });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { refreshed_urls = refreshed }));
        });

        // 120 distinct URLs, all carrying stale signature params.
        var stored = Enumerable.Range(0, 120)
            .Select(i => $"https://cdn.discordapp.com/attachments/1/{i}/f{i}.png?ex=old&hm=sig")
            .ToList();

        var map = await service.RefreshAsync(stored, CancellationToken.None);

        Assert.Equal(3, requests.Count);
        Assert.All(requests, batch => Assert.True(batch.Count <= 50));
        Assert.All(requests.SelectMany(b => b), url => Assert.DoesNotContain("?", url));
        Assert.Equal(120, map.Count);
        var key = AttachmentUrlRefreshService.StripQuery(stored[7]);
        Assert.Equal(key + "?ex=fresh", map[key]);
    }

    [Fact]
    public async Task RefreshAsync_WhenOneBatchFails_KeepsOtherBatches()
    {
        var call = 0;
        var service = NewService(async req =>
        {
            call++;
            if (call == 1)
                return Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");

            var body = await req.Content!.ReadAsStringAsync();
            var sent = JsonSerializer.Deserialize<JsonElement>(body)
                .GetProperty("attachment_urls").EnumerateArray().Select(e => e.GetString()!).ToList();
            var refreshed = sent.Select(u => new { original = u, refreshed = u + "?ex=fresh" });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { refreshed_urls = refreshed }));
        });

        var stored = Enumerable.Range(0, 60)
            .Select(i => $"https://cdn.discordapp.com/attachments/1/{i}/f{i}.png")
            .ToList();

        var map = await service.RefreshAsync(stored, CancellationToken.None);

        // First batch of 50 lost, second batch of 10 mapped.
        Assert.Equal(10, map.Count);
    }

    [Fact]
    public async Task RefreshAsync_SkipsEntriesDiscordDeclinedToRefresh()
    {
        var service = NewService(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            var sent = JsonSerializer.Deserialize<JsonElement>(body)
                .GetProperty("attachment_urls").EnumerateArray().Select(e => e.GetString()!).ToList();
            var refreshed = sent.Take(1).Select(u => new { original = u, refreshed = u + "?ex=fresh" });
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { refreshed_urls = refreshed }));
        });

        var map = await service.RefreshAsync(
            ["https://cdn.discordapp.com/a/1.png", "https://cdn.discordapp.com/a/2.png"],
            CancellationToken.None);

        Assert.Single(map);
    }

    private static AttachmentUrlRefreshService NewService(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new AttachmentUrlRefreshService(
            new StubHttpClientFactory(new StubHandler(respond)),
            Options.Create(new DiscordOptions { Token = new string('x', 60) }),
            NullLogger<AttachmentUrlRefreshService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://discord.test/api/v10/") };
    }
}
