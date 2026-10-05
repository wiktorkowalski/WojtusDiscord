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

    // How long a thumbnail request waits for its turn before it answers 503.
    public static readonly TimeSpan RefreshWaitTimeout = TimeSpan.FromSeconds(10);

    // How long one computed answer of GET api/stats/memes is served. The waiting count in it
    // is up to a minute old already (MemeIndexSummaryReader).
    public static readonly TimeSpan IndexCacheDuration = TimeSpan.FromSeconds(30);

    private IndexSnapshot? _index;

    // One request computes the index answer; the others wait and read what it kept.
    public SemaphoreSlim IndexGate { get; } = new(1, 1);

    public SemaphoreSlim SearchGate { get; } = new(MaxConcurrentSearches, MaxConcurrentSearches);

    // One refresh at a time. A request that waited reads the cache again before it calls
    // Discord, so requests for one image at the same time make one call.
    public SemaphoreSlim RefreshGate { get; } = new(1, 1);

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
        SearchGate.Dispose();
        RefreshGate.Dispose();
        RefreshBudget.Dispose();
        Thumbnails.Dispose();
    }

    private sealed record IndexSnapshot(MemeIndexDto Index, DateTime ExpiresAtUtc);
}
