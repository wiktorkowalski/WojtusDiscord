using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

// Trailing defaults keep pre-#221 benchmark links files deserializable — old exports lack MessageId/FileSizeBytes.
public sealed record MemeSampleItem(
    ulong GuildDiscordId,
    ulong ChannelDiscordId,
    ulong MessageDiscordId,
    ulong AttachmentDiscordId,
    string FileName,
    DateTime CreatedAtUtc,
    string StoredUrl,
    Guid MessageId = default,
    long FileSizeBytes = 0);

public sealed class MemeSampleService(
    DiscordDbContext db,
    IOptions<MemeIndexOptions> options,
    ILogger<MemeSampleService> logger)
{
    // Matches the 4-field shape MessageEventHandler/MessagesBackfillJob serialize.
    private sealed record StoredAttachment(ulong Id, string? Url, string? FileName, int FileSize);

    public async Task<List<MemeSampleItem>> SampleAsync(int sampleSize, CancellationToken cancellationToken)
    {
        var candidates = await GetCandidatesAsync(cancellationToken);
        var picked = Stratify(candidates, sampleSize);

        logger.LogInformation(
            "Sampled {Picked} of requested {Requested} image attachments from {Candidates} candidates",
            picked.Count, sampleSize, candidates.Count);

        return picked;
    }

    // Round-robin across years so old low-res memes are represented, not drowned out by recent years.
    public static List<MemeSampleItem> Stratify(IReadOnlyCollection<MemeSampleItem> candidates, int sampleSize)
    {
        var byYear = candidates
            .GroupBy(c => c.CreatedAtUtc.Year)
            .OrderBy(g => g.Key)
            .Select(g => new Queue<MemeSampleItem>(g.OrderBy(_ => Random.Shared.Next())))
            .ToList();

        var picked = new List<MemeSampleItem>(sampleSize);
        while (picked.Count < sampleSize && byYear.Any(q => q.Count > 0))
        {
            foreach (var yearQueue in byYear)
            {
                if (picked.Count >= sampleSize)
                    break;
                if (yearQueue.TryDequeue(out var item))
                    picked.Add(item);
            }
        }

        return picked;
    }

    public Task<List<MemeSampleItem>> GetCandidatesAsync(CancellationToken cancellationToken)
        => GetCandidatesCoreAsync(messageDiscordId: null, cancellationToken);

    public Task<List<MemeSampleItem>> GetCandidatesForMessageAsync(
        ulong messageDiscordId, CancellationToken cancellationToken)
        => GetCandidatesCoreAsync(messageDiscordId, cancellationToken);

    private async Task<List<MemeSampleItem>> GetCandidatesCoreAsync(
        ulong? messageDiscordId, CancellationToken cancellationToken)
    {
        var channelIds = options.Value.ChannelIds;

        var query =
            from m in db.Messages.AsNoTracking()
            join c in db.Channels.AsNoTracking() on m.ChannelId equals c.Id
            join g in db.Guilds.AsNoTracking() on m.GuildId equals g.Id
            where channelIds.Contains(c.DiscordId)
                  && m.HasAttachments
                  && !m.IsDeleted
                  && m.AttachmentsJson != null
            select new
            {
                m.Id,
                m.DiscordId,
                m.AttachmentsJson,
                m.CreatedAtUtc,
                ChannelDiscordId = c.DiscordId,
                GuildDiscordId = g.DiscordId
            };

        if (messageDiscordId is { } id)
            query = query.Where(r => r.DiscordId == id);

        var rows = await query.ToListAsync(cancellationToken);

        var candidates = new List<MemeSampleItem>();
        foreach (var row in rows)
        {
            List<StoredAttachment>? attachments;
            try
            {
                attachments = JsonSerializer.Deserialize<List<StoredAttachment>>(row.AttachmentsJson!);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Unparseable attachments_json on message {MessageId}", row.DiscordId);
                continue;
            }

            if (attachments is null)
                continue;

            candidates.AddRange(attachments
                .Where(a => a.FileName is not null && a.Url is not null && ImageMagic.IsIndexableFileName(a.FileName))
                .Select(a => new MemeSampleItem(
                    row.GuildDiscordId,
                    row.ChannelDiscordId,
                    row.DiscordId,
                    a.Id,
                    a.FileName!,
                    row.CreatedAtUtc,
                    a.Url!,
                    row.Id,
                    a.FileSize)));
        }

        return candidates;
    }
}
