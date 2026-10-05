using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

// Trailing defaults keep pre-#221 benchmark links files deserializable — old exports lack MessageId/FileSizeBytes.
internal sealed record MemeSampleItem(
    ulong GuildDiscordId,
    ulong ChannelDiscordId,
    ulong MessageDiscordId,
    ulong AttachmentDiscordId,
    string FileName,
    DateTime CreatedAtUtc,
    string StoredUrl,
    Guid MessageId = default,
    long FileSizeBytes = 0);

// OldestPostedAtUtc is null when nothing waits.
internal sealed record MemeWaiting(int Count, DateTime? OldestPostedAtUtc);

internal sealed class MemeSampleService(
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
    // When every candidate fits there is nothing to choose: return them in the given order, so a
    // fixed-ids links file gives the same sample on every run (#366).
    public static List<MemeSampleItem> Stratify(IReadOnlyCollection<MemeSampleItem> candidates, int sampleSize)
    {
        if (candidates.Count <= sampleSize)
            return [.. candidates];

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

    // Waiting (#397) = a candidate the annotation writer could take and that has no meme_index
    // row. Any row counts as handled, whatever its status. One number for all guilds.
    public async Task<int> CountWaitingAsync(CancellationToken cancellationToken) =>
        (await GetWaitingAsync(cancellationToken)).Count;

    // The same set as the count, with the post date of its oldest image (#395).
    public async Task<MemeWaiting> GetWaitingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
            return new MemeWaiting(0, null);

        var takeable = (await GetCandidatesAsync(cancellationToken))
            .Where(c => !ImageMagic.IsGifFileName(c.FileName) && !options.Value.ExceedsMaxImageBytes(c.FileSizeBytes))
            .ToList();

        var known = (await db.MemeIndex.AsNoTracking()
                .Select(m => m.AttachmentDiscordId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var waiting = takeable.Where(c => !known.Contains(c.AttachmentDiscordId)).ToList();
        return new MemeWaiting(waiting.Count, waiting.Count == 0 ? null : waiting.Min(c => c.CreatedAtUtc));
    }

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
            var attachments = ParseIndexableAttachments(row.AttachmentsJson!, row.DiscordId);

            candidates.AddRange(attachments
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

    private List<StoredAttachment> ParseIndexableAttachments(string attachmentsJson, ulong messageDiscordId)
    {
        List<StoredAttachment>? attachments;
        try
        {
            attachments = JsonSerializer.Deserialize<List<StoredAttachment>>(attachmentsJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Unparseable attachments_json on message {MessageId}", messageDiscordId);
            return [];
        }

        return attachments
            ?.Where(a => a.FileName is not null && a.Url is not null && ImageMagic.IsIndexableFileName(a.FileName))
            .ToList() ?? [];
    }
}
