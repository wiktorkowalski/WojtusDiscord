using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services.MemeIndexing;

public sealed record MemeIndexSummary(long Indexed, long Waiting);

// Public because StatsController is public; the reader itself stays internal.
public interface IMemeIndexSummaryReader
{
    Task<MemeIndexSummary> GetAsync(CancellationToken cancellationToken);
}

internal sealed class MemeIndexSummaryReader(
    DiscordDbContext db,
    MemeSampleService sampleService,
    IMemoryCache cache) : IMemeIndexSummaryReader
{
    // The waiting count reads every message with attachments of the meme channels. The dashboard
    // asks on every page load and window focus; a new meme may show up a minute late there.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private const string CacheKey = "meme-index-summary";

    // Static because the reader is scoped: every request has its own instance, and after an
    // expiry only one of them may run the scan. The others wait and read what it cached.
    private static readonly SemaphoreSlim ComputeGate = new(1, 1);

    public async Task<MemeIndexSummary> GetAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out MemeIndexSummary? cached) && cached is not null)
            return cached;

        await ComputeGate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have filled the cache while this one waited.
            if (cache.TryGetValue(CacheKey, out cached) && cached is not null)
                return cached;

            var indexed = await db.MemeIndex.AsNoTracking()
                .LongCountAsync(m => m.Status == MemeIndexStatus.Indexed, cancellationToken);
            var waiting = await sampleService.CountWaitingAsync(cancellationToken);

            var summary = new MemeIndexSummary(indexed, waiting);
            cache.Set(CacheKey, summary, CacheDuration);
            return summary;
        }
        finally
        {
            ComputeGate.Release();
        }
    }
}
