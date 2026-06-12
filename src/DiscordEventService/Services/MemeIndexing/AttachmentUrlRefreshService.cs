using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordEventService.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

internal sealed class AttachmentUrlRefreshService(
    IHttpClientFactory httpClientFactory,
    IOptions<DiscordOptions> discordOptions,
    ILogger<AttachmentUrlRefreshService> logger)
{
    public const string HttpClientName = "discord-api";

    private const int BatchSize = 50;
    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromMilliseconds(300);

    public async Task<Dictionary<string, string>> RefreshAsync(
        IReadOnlyCollection<string> storedUrls,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        var distinct = storedUrls.Select(StripQuery).Distinct().ToList();

        for (var offset = 0; offset < distinct.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = distinct.Skip(offset).Take(BatchSize).ToList();

            try
            {
                await RefreshBatchAsync(batch, result, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Lose this batch only; its URLs surface downstream as skips.
                logger.LogWarning(ex, "Attachment URL refresh batch failed for {UrlCount} urls", batch.Count);
            }

            if (offset + BatchSize < distinct.Count)
                await Task.Delay(DelayBetweenBatches, cancellationToken);
        }

        logger.LogInformation("Refreshed {RefreshedCount} of {RequestedCount} attachment URLs", result.Count, distinct.Count);
        return result;
    }

    // The signature params are exactly what's stale — match on the bare URL.
    public static string StripQuery(string url)
    {
        var queryStart = url.IndexOf('?');
        return queryStart < 0 ? url : url[..queryStart];
    }

    private async Task RefreshBatchAsync(
        List<string> batch,
        Dictionary<string, string> result,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "attachments/refresh-urls");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bot {discordOptions.Value.Token}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { attachment_urls = batch }),
            Encoding.UTF8,
            "application/json");

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("attachments/refresh-urls returned {StatusCode}: {Body}",
                (int)response.StatusCode, body.Length <= 300 ? body : body[..300]);
            return;
        }

        var parsed = JsonSerializer.Deserialize<RefreshResponse>(body);
        foreach (var entry in parsed?.RefreshedUrls ?? [])
        {
            if (entry.Original is not null && !string.IsNullOrEmpty(entry.Refreshed))
                result[StripQuery(entry.Original)] = entry.Refreshed;
        }
    }

    private sealed record RefreshResponse(
        [property: JsonPropertyName("refreshed_urls")] List<RefreshedUrl>? RefreshedUrls);

    private sealed record RefreshedUrl(
        [property: JsonPropertyName("original")] string? Original,
        [property: JsonPropertyName("refreshed")] string? Refreshed);
}
