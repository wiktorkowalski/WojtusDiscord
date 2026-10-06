using System.Globalization;
using System.Threading.RateLimiting;
using System.Web;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

// Url set = a Discord CDN URL that works now. Url null = no image: not servable, or, with
// IsRetryable, no answer right now (Discord failed or a cap is reached) and a later request may get one.
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
// No bytes pass through this service.
//
// The endpoint has no auth, so the rules are strict:
// - The caller sends an attachment id only. The URL comes from the database.
// - Servable = the row is Indexed, its channel is a meme channel NOW, and the message is not
//   deleted and still holds the attachment. Everything else is "not found".
// - The database decides that on every request, before the cache is read and before any call
//   to Discord. The cache holds answers of Discord only, so it cannot serve an image the
//   database no longer allows (#408).
// - The only outbound call goes to the Discord API, with a URL of the Discord CDN; the
//   redirect goes to the Discord CDN only.
// - Calls to Discord are capped: MemeDashboardLimits.
internal sealed class MemeThumbnailResolver(
    DiscordDbContext db,
    MemeSampleService sampleService,
    AttachmentUrlRefreshService urlRefreshService,
    MemeDashboardLimits limits,
    IOptions<MemeIndexOptions> options,
    ILogger<MemeThumbnailResolver> logger) : IMemeThumbnailResolver
{
    // A signed URL lives about a day. It is kept for an hour, and never past ExpiryMargin
    // before its own expiry (the ex parameter), so a browser that reuses the redirect for
    // BrowserMaxAge still gets a URL that works.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BrowserMaxAge = TimeSpan.FromMinutes(10);

    // Discord gave no usable URL (it declined, or the URL is not a Discord CDN URL): Discord is
    // asked again after this. A "not servable" from the database is not kept at all.
    public static readonly TimeSpan DeclinedCacheDuration = TimeSpan.FromMinutes(5);

    // Discord failed: no new call for this image for this long.
    public static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(30);

    // The year 10000 in Unix seconds: past it FromUnixTimeSeconds throws.
    private const long MaxUnixSeconds = 253402300800L;

    private static readonly string[] DiscordCdnHosts = ["cdn.discordapp.com", "media.discordapp.net"];

    public async Task<MemeThumbnail> ResolveAsync(ulong attachmentDiscordId, CancellationToken cancellationToken)
    {
        // Before the cache: a kept URL must not outlive the message, the Indexed status or the
        // channel's place in the list. The cost is two indexed reads per request.
        var storedUrl = await FindServableStoredUrlAsync(attachmentDiscordId, cancellationToken);

        // Not cached: the next request asks the database again anyway, and ids with no row
        // have no bound. The check above already keeps a kept URL from being served; the
        // removal makes an image that becomes servable again ask Discord again.
        if (storedUrl is null)
        {
            limits.Thumbnails.Remove(attachmentDiscordId);
            return MemeThumbnail.Gone;
        }

        if (Cached(attachmentDiscordId) is { } cached)
            return cached;

        // One call to Discord at a time. No slot (the queue is full, or the wait was too long): 503.
        using var slot = await WaitForRefreshSlotAsync(cancellationToken);
        if (slot is not { IsAcquired: true })
            return MemeThumbnail.Unavailable;

        // A request for the same image may have filled the cache while this one waited.
        if (Cached(attachmentDiscordId) is { } filledMeanwhile)
            return filledMeanwhile;

        using var permit = limits.RefreshBudget.AttemptAcquire();
        if (!permit.IsAcquired)
            return MemeThumbnail.Unavailable;

        return await RefreshAsync(attachmentDiscordId, storedUrl, cancellationToken);
    }

    // Null = the wait reached RefreshWaitTimeout. A lease that is not acquired = the queue is full.
    private async Task<RateLimitLease?> WaitForRefreshSlotAsync(CancellationToken cancellationToken)
    {
        using var waitLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitLimit.CancelAfter(MemeDashboardLimits.RefreshWaitTimeout);

        try
        {
            return await limits.RefreshSlots.AcquireAsync(1, waitLimit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // Null = not servable: no row, a row that is not Indexed, a channel outside the meme
    // channels, or no stored URL (FindStoredUrlAsync).
    private async Task<string?> FindServableStoredUrlAsync(ulong attachmentDiscordId, CancellationToken cancellationToken)
    {
        var row = await db.MemeIndex.AsNoTracking()
            .Where(m => m.AttachmentDiscordId == attachmentDiscordId)
            .Select(m => new { m.Status, m.ChannelDiscordId, m.MessageDiscordId })
            .FirstOrDefaultAsync(cancellationToken);

        return row is not null
            && row.Status == MemeIndexStatus.Indexed
            && options.Value.ChannelIds.Contains(row.ChannelDiscordId)
                ? await FindStoredUrlAsync(attachmentDiscordId, row.MessageDiscordId, cancellationToken)
                : null;
    }

    // The stored URL, by the same parse as the indexer: nothing comes back for a deleted
    // message, for a channel outside the meme channels, or when the message no longer holds
    // the attachment.
    private async Task<string?> FindStoredUrlAsync(
        ulong attachmentDiscordId, ulong messageDiscordId, CancellationToken cancellationToken)
    {
        var storedUrl = (await sampleService.GetCandidatesForMessageAsync(messageDiscordId, cancellationToken))
            .FirstOrDefault(c => c.AttachmentDiscordId == attachmentDiscordId)
            ?.StoredUrl;

        return storedUrl is not null && IsDiscordCdnUrl(storedUrl, out _) ? storedUrl : null;
    }

    private async Task<MemeThumbnail> RefreshAsync(ulong attachmentDiscordId, string storedUrl, CancellationToken cancellationToken)
    {
        using var callLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callLimit.CancelAfter(MemeDashboardLimits.RefreshCallTimeout);

        AttachmentUrlRefreshResult refreshed;
        try
        {
            // The refresh service logs its own failures and its own count.
            refreshed = await urlRefreshService.RefreshAsync([storedUrl], callLimit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Not logged by the refresh service: for it, this is the caller's cancellation.
            logger.LogWarning(
                "Attachment URL refresh for meme attachment {AttachmentId} took over {TimeoutSeconds} s; no thumbnail served",
                attachmentDiscordId, MemeDashboardLimits.RefreshCallTimeout.TotalSeconds);
            return Keep(attachmentDiscordId, MemeThumbnail.Unavailable, FailureCacheDuration);
        }

        switch (refreshed.GetFreshUrl(storedUrl, out var freshUrl))
        {
            case AttachmentUrlRefreshOutcome.BatchFailed:
                return Keep(attachmentDiscordId, MemeThumbnail.Unavailable, FailureCacheDuration);
            case AttachmentUrlRefreshOutcome.Declined:
                return Keep(attachmentDiscordId, MemeThumbnail.Gone, DeclinedCacheDuration);
        }

        // The redirect target is whatever came back: send the browser to the Discord CDN only.
        if (!IsDiscordCdnUrl(freshUrl!, out var uri))
        {
            logger.LogWarning(
                "Refreshed URL of meme attachment {AttachmentId} is not an https Discord CDN URL; no thumbnail served",
                attachmentDiscordId);
            return Keep(attachmentDiscordId, MemeThumbnail.Gone, DeclinedCacheDuration);
        }

        var thumbnail = new MemeThumbnail(freshUrl, IsRetryable: false);
        var keepFor = ExpiryOf(uri) is { } expiresAtUtc
            ? TimeSpan.FromTicks(Math.Min(CacheDuration.Ticks, (expiresAtUtc - ExpiryMargin - DateTime.UtcNow).Ticks))
            : CacheDuration;

        // A URL that is about to expire is served once and not kept.
        return keepFor > TimeSpan.Zero ? Keep(attachmentDiscordId, thumbnail, keepFor) : thumbnail;
    }

    private MemeThumbnail? Cached(ulong attachmentDiscordId) => limits.Thumbnails.Get<MemeThumbnail>(attachmentDiscordId);

    private MemeThumbnail Keep(ulong attachmentDiscordId, MemeThumbnail thumbnail, TimeSpan duration)
    {
        limits.Thumbnails.Set(
            attachmentDiscordId,
            thumbnail,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration, Size = 1 });
        return thumbnail;
    }

    private static bool IsDiscordCdnUrl(string url, out Uri uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri!)
        && uri.Scheme == Uri.UriSchemeHttps
        && DiscordCdnHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    // Discord puts the expiry in the ex parameter: Unix seconds, in hex.
    private static DateTime? ExpiryOf(Uri uri) =>
        long.TryParse(
            HttpUtility.ParseQueryString(uri.Query)["ex"], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seconds)
        && seconds is > 0 and < MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}
