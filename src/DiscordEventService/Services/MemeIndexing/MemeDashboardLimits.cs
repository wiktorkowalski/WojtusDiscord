using System.Threading.RateLimiting;
using DiscordEventService.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services.MemeIndexing;

// The caps of the dashboard's meme page (#395). The dashboard API has no auth, so nothing a
// caller sends may make this service work without a bound: not Postgres (the tester search
// scans the whole corpus), not the Discord API (a thumbnail asks Discord to sign a URL).
// A singleton: the caps are for the process, not for a request.
internal sealed class MemeDashboardLimits : IDisposable
{
    // Tester searches that may run at one time. One more gets 429.
    public const int MaxConcurrentSearches = 2;

    // Signed thumbnail URLs kept in memory. An entry is one short string.
    public const int MaxCachedThumbnails = 2000;

    // Calls to Discord's attachments/refresh-urls from the thumbnail path, per minute.
    public const int MaxRefreshesPerMinute = 120;

    // Thumbnail requests that may wait for their turn to call Discord. One more gets 503 at once.
    public const int MaxQueuedRefreshes = 32;

    // How long a thumbnail request waits for its turn before it answers 503.
    public static readonly TimeSpan RefreshWaitTimeout = TimeSpan.FromSeconds(10);

    // How long one call to Discord may take. It holds the only slot, so it is far below the
    // HTTP client's own timeout.
    public static readonly TimeSpan RefreshCallTimeout = TimeSpan.FromSeconds(5);

    // How long one computed answer of GET api/stats/memes is served. The waiting count in it
    // is up to a minute old already (MemeIndexSummaryReader).
    public static readonly TimeSpan IndexCacheDuration = TimeSpan.FromSeconds(30);

    // Answers of GET api/stats/memes/search-usage kept at one time: one per value of days.
    // Above the 365 values a caller can ask for, so the limit is a bound, not a policy.
    public const int MaxCachedSearchUsages = 512;

    private IndexSnapshot? _index;

    // One request computes a search-usage answer; the others wait and read what it kept.
    public SemaphoreSlim SearchUsageGate { get; } = new(1, 1);

    // Keyed by days, each entry kept for IndexCacheDuration.
    public MemoryCache SearchUsage { get; } = new(new MemoryCacheOptions { SizeLimit = MaxCachedSearchUsages });

    // One request computes the index answer; the others wait and read what it kept.
    public SemaphoreSlim IndexGate { get; } = new(1, 1);

    public SemaphoreSlim SearchGate { get; } = new(MaxConcurrentSearches, MaxConcurrentSearches);

    // One refresh at a time, with a queue that has a limit. A request that waited reads the
    // cache again before it calls Discord, so requests for one image at the same time make one
    // call. A waiter that gives up (timeout, client gone) leaves the queue; a lease is
    // released when it is disposed.
    public RateLimiter RefreshSlots { get; } = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = 1,
        QueueLimit = MaxQueuedRefreshes,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public RateLimiter RefreshBudget { get; } = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
    {
        PermitLimit = MaxRefreshesPerMinute,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    });

    // Its own cache, with a size limit: the application's shared IMemoryCache has none.
    public MemoryCache Thumbnails { get; } = new(new MemoryCacheOptions { SizeLimit = MaxCachedThumbnails });

    public MemeIndexDto? FreshIndex() =>
        Volatile.Read(ref _index) is { } snapshot && snapshot.ExpiresAtUtc > DateTime.UtcNow ? snapshot.Index : null;

    public void KeepIndex(MemeIndexDto index) =>
        Volatile.Write(ref _index, new IndexSnapshot(index, DateTime.UtcNow + IndexCacheDuration));

    public void Dispose()
    {
        IndexGate.Dispose();
        SearchUsageGate.Dispose();
        SearchUsage.Dispose();
        SearchGate.Dispose();
        RefreshSlots.Dispose();
        RefreshBudget.Dispose();
        Thumbnails.Dispose();
    }

    private sealed record IndexSnapshot(MemeIndexDto Index, DateTime ExpiresAtUtc);
}
