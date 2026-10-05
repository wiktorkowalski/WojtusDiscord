using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services.MemeIndexing;

// Url set = a Discord CDN URL that works now. Url null = no image: gone for good, or, with
// IsRetryable, Discord did not answer and a later request may get one.
public sealed record MemeThumbnail(string? Url, bool IsRetryable)
{
    internal static readonly MemeThumbnail Gone = new(null, IsRetryable: false);
    internal static readonly MemeThumbnail Unavailable = new(null, IsRetryable: true);
}

// Public because MemeStatsController is public; the resolver itself stays internal.
public interface IMemeThumbnailResolver
{
    Task<MemeThumbnail> ResolveAsync(ulong attachmentDiscordId, CancellationToken cancellationToken);
}

// The image of an indexed meme for the dashboard (#395). meme_index stores no URL and the URL
// in messages.attachments_json has an expired signature, so Discord signs it again
// (attachments/refresh-urls, as the indexer does) and the browser loads the image from the CDN.
// No bytes pass through this service. The id is looked up in meme_index and the URL comes
// from the database: a caller cannot make this service ask for a URL of its choice.
internal sealed class MemeThumbnailResolver(
    DiscordDbContext db,
    MemeSampleService sampleService,
    AttachmentUrlRefreshService urlRefreshService,
    IMemoryCache cache,
    ILogger<MemeThumbnailResolver> logger) : IMemeThumbnailResolver
{
    // A signed URL lives about a day. One hour here plus one hour in the browser stays far inside it.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private static readonly string[] DiscordCdnHosts = ["cdn.discordapp.com", "media.discordapp.net"];

    public async Task<MemeThumbnail> ResolveAsync(ulong attachmentDiscordId, CancellationToken cancellationToken)
    {
        var cacheKey = $"meme-thumbnail:{attachmentDiscordId}";
        if (cache.TryGetValue(cacheKey, out MemeThumbnail? cached) && cached is not null)
            return cached;

        var messageDiscordId = await db.MemeIndex.AsNoTracking()
            .Where(m => m.AttachmentDiscordId == attachmentDiscordId)
            .Select(m => (ulong?)m.MessageDiscordId)
            .FirstOrDefaultAsync(cancellationToken);

        // Not cached: the ids with no row have no bound, the ids with a row do.
        if (messageDiscordId is not { } messageId)
            return MemeThumbnail.Gone;

        // The stored URL, by the same parse as the indexer. Nothing comes back for a deleted
        // message or for a channel that is no longer a meme channel.
        var storedUrl = (await sampleService.GetCandidatesForMessageAsync(messageId, cancellationToken))
            .FirstOrDefault(c => c.AttachmentDiscordId == attachmentDiscordId)
            ?.StoredUrl;

        var thumbnail = storedUrl is null
            ? MemeThumbnail.Gone
            : await RefreshAsync(attachmentDiscordId, storedUrl, cancellationToken);

        // A failed Discord call is not an answer about the attachment: the next request asks again.
        if (!thumbnail.IsRetryable)
            cache.Set(cacheKey, thumbnail, CacheDuration);

        return thumbnail;
    }

    private async Task<MemeThumbnail> RefreshAsync(ulong attachmentDiscordId, string storedUrl, CancellationToken cancellationToken)
    {
        // The refresh service logs its own failures and its own count.
        var refreshed = await urlRefreshService.RefreshAsync([storedUrl], cancellationToken);

        switch (refreshed.GetFreshUrl(storedUrl, out var freshUrl))
        {
            case AttachmentUrlRefreshOutcome.BatchFailed:
                return MemeThumbnail.Unavailable;
            case AttachmentUrlRefreshOutcome.Declined:
                return MemeThumbnail.Gone;
        }

        // The redirect target is whatever came back: send the browser to the Discord CDN only.
        if (!Uri.TryCreate(freshUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !DiscordCdnHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Refreshed URL of meme attachment {AttachmentId} is not an https Discord CDN URL; no thumbnail served",
                attachmentDiscordId);
            return MemeThumbnail.Gone;
        }

        return new MemeThumbnail(freshUrl, IsRetryable: false);
    }
}
