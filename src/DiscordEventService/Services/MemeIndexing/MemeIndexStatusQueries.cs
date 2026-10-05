using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.MemeIndexing;

// How far one writer got: a (model, prompt version) that has annotations.
internal sealed record MemeWriterCount(string ModelId, string PromptVersion, int Count, DateTime LastIndexedAtUtc);

// The two aggregations behind GET /api/ops/meme-index/status, shared with the dashboard's
// meme page (#395) so both report the same numbers.
internal static class MemeIndexStatusQueries
{
    public static Task<Dictionary<MemeIndexStatus, int>> CountByStatusAsync(
        DiscordDbContext db, CancellationToken cancellationToken) =>
        db.MemeIndex.AsNoTracking()
            .GroupBy(m => m.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public static async Task<List<MemeWriterCount>> CountByWriterAsync(
        DiscordDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.MemeAnnotations.AsNoTracking()
            .GroupBy(a => new { a.ModelId, a.PromptVersion })
            .Select(g => new
            {
                g.Key.ModelId,
                g.Key.PromptVersion,
                Count = g.Count(),
                LastIndexedAtUtc = g.Max(a => a.IndexedAtUtc),
            })
            .OrderBy(w => w.ModelId)
            .ThenBy(w => w.PromptVersion)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(w => new MemeWriterCount(w.ModelId, w.PromptVersion, w.Count, w.LastIndexedAtUtc))];
    }
}
