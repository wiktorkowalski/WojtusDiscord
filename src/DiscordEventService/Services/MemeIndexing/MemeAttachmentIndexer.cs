using System.Security.Cryptography;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

internal sealed class MemeIndexRunCounters
{
    public int Indexed { get; set; }
    public int Deduped { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int ModelCalls { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public decimal CostUsd { get; set; }
}

// The DbContext stays a parameter — callers own the unit of work and decide when to flush.
internal sealed class MemeAttachmentIndexer(
    OpenRouterClient openRouterClient,
    IHttpClientFactory httpClientFactory,
    IOptions<MemeIndexOptions> memeIndexOptions,
    IOptions<OpenRouterOptions> openRouterOptions,
    ILogger<MemeAttachmentIndexer> logger)
{
    public async Task<MemeIndexEntity> GetOrCreateRowAsync(
        DiscordDbContext db, MemeSampleItem item, CancellationToken cancellationToken)
    {
        var row = await db.MemeIndex
            .FirstOrDefaultAsync(m => m.AttachmentDiscordId == item.AttachmentDiscordId, cancellationToken);
        if (row is not null)
            return row;

        row = new MemeIndexEntity
        {
            MessageId = item.MessageId,
            GuildDiscordId = item.GuildDiscordId,
            ChannelDiscordId = item.ChannelDiscordId,
            MessageDiscordId = item.MessageDiscordId,
            AttachmentDiscordId = item.AttachmentDiscordId,
            FileName = item.FileName,
            FileSizeBytes = item.FileSizeBytes,
            Status = MemeIndexStatus.Pending,
        };
        db.MemeIndex.Add(row);
        return row;
    }

    public async Task ProcessOneAsync(
        DiscordDbContext db,
        MemeIndexEntity row,
        MemeSampleItem item,
        AttachmentUrlRefreshResult freshUrls,
        MemeIndexRunCounters counters,
        CancellationToken cancellationToken)
    {
        var memeOptions = memeIndexOptions.Value;
        var openRouter = openRouterOptions.Value;

        // Discord metadata already carries the size — pre-skip oversized files
        // before spending a download. Deterministic, so no attempt is charged.
        // (0 = unknown size from pre-#221 data; those still go through the
        // post-download check below.)
        if (item.FileSizeBytes > memeOptions.MaxImageBytes)
        {
            Skip(row, counters, $"unsupported: image too large ({item.FileSizeBytes} bytes per metadata)");
            return;
        }

        var refreshOutcome = freshUrls.GetFreshUrl(item.StoredUrl, out var freshUrl);
        if (refreshOutcome == AttachmentUrlRefreshOutcome.BatchFailed)
        {
            // Transient refresh failure — the attachment itself was never
            // attempted, so this must stay retryable without burning one of
            // the sweep's capped attempts.
            Fail(row, counters, "transient: refresh-urls batch failed");
            return;
        }

        row.AttemptCount++;

        if (refreshOutcome == AttachmentUrlRefreshOutcome.Declined)
        {
            Skip(row, counters, "dead attachment: refresh-urls declined to re-sign");
            return;
        }

        var imageBytes = await DownloadImageAsync(freshUrl!, row, counters, cancellationToken);
        if (imageBytes is null) return;

        row.FileSizeBytes = imageBytes.Length;

        if (imageBytes.Length > memeOptions.MaxImageBytes)
        {
            Skip(row, counters, $"unsupported: image too large ({imageBytes.Length} bytes)");
            return;
        }

        var mimeType = ImageMagic.SniffMimeType(imageBytes);
        if (mimeType is null)
        {
            Skip(row, counters, "unsupported: bytes are not a recognized image format");
            return;
        }
        row.ContentType = mimeType;

        var contentHash = Convert.ToHexStringLower(SHA256.HashData(imageBytes));
        row.ContentHash = contentHash;

        if (await TryDedupeByContentHashAsync(db, row, contentHash, counters, cancellationToken))
            return;

        var result = await openRouterClient.AnalyzeImageAsync(imageBytes, mimeType, openRouter.Model, cancellationToken);
        counters.ModelCalls++;
        counters.PromptTokens += result.Usage?.PromptTokens ?? 0;
        counters.CompletionTokens += result.Usage?.CompletionTokens ?? 0;
        counters.CostUsd += result.Usage?.CostUsd ?? 0;

        ApplyAnalysisResult(row, result, openRouter.Model, counters);

        await Task.Delay(TimeSpan.FromMilliseconds(openRouter.RequestDelayMs), cancellationToken);
    }

    // Returns null when the row was already terminally marked (Skip on dead/oversized
    // attachment, Fail on transient download error) and processing should stop.
    private async Task<byte[]?> DownloadImageAsync(
        string freshUrl, MemeIndexEntity row, MemeIndexRunCounters counters, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(MemeBenchmarkJob.DownloadHttpClientName);
            return await client.GetByteArrayAsync(freshUrl, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Gone)
        {
            Skip(row, counters, $"dead attachment: download HTTP {(int)ex.StatusCode!}");
            return null;
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            // The discord-cdn client caps buffering at MaxImageBytes; blowing it is
            // deterministic (metadata lied small), so terminal Skip — a transient
            // Failed would be refunded and retried by every sweep forever.
            Skip(row, counters, "unsupported: image too large (exceeded download buffer cap)");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            FailTransient(row, counters, $"transient: download failed: {ex.Message}");
            return null;
        }
    }

    // Repost dedupe: same bytes already Indexed → copy its metadata, no
    // model call. RawResponseJson stays null — provenance lives on the
    // original row, found via the shared content_hash.
    private async Task<bool> TryDedupeByContentHashAsync(
        DiscordDbContext db, MemeIndexEntity row, string contentHash, MemeIndexRunCounters counters, CancellationToken cancellationToken)
    {
        var original = await db.MemeIndex.AsNoTracking()
            .Where(m => m.ContentHash == contentHash && m.Status == MemeIndexStatus.Indexed && m.Id != row.Id)
            .Select(m => new { m.DescriptionPl, m.DescriptionEn, m.OcrText, m.Tags, m.Source, m.Template, m.ModelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (original is null) return false;

        row.DescriptionPl = original.DescriptionPl;
        row.DescriptionEn = original.DescriptionEn;
        row.OcrText = original.OcrText;
        row.Tags = original.Tags;
        row.Source = original.Source;
        row.Template = original.Template;
        row.ModelId = original.ModelId;
        row.RawResponseJson = null;
        row.IndexedAtUtc = DateTime.UtcNow;
        row.Status = MemeIndexStatus.Indexed;
        row.Error = null;
        counters.Deduped++;
        logger.LogDebug("Meme attachment {AttachmentId} deduped via content hash {ContentHash}",
            row.AttachmentDiscordId, contentHash);
        return true;
    }

    private void ApplyAnalysisResult(MemeIndexEntity row, MemeAnalysisResult result, string model, MemeIndexRunCounters counters)
    {
        switch (result.Outcome)
        {
            case MemeAnalysisOutcome.Success:
                row.DescriptionPl = result.Metadata!.DescriptionPl;
                row.DescriptionEn = result.Metadata.DescriptionEn;
                row.OcrText = result.Metadata.OcrText;
                row.Tags = result.Metadata.Tags;
                row.Source = result.Metadata.Source;
                row.Template = result.Metadata.Template;
                row.ModelId = model;
                row.RawResponseJson = result.RawContent;
                row.IndexedAtUtc = DateTime.UtcNow;
                row.Status = MemeIndexStatus.Indexed;
                row.Error = null;
                counters.Indexed++;
                break;

            case MemeAnalysisOutcome.Refusal:
                Skip(row, counters, $"model refusal: {result.Error}");
                break;

            default:
                if (result.IsTransient)
                    FailTransient(row, counters, $"transient: {result.Error ?? "unknown analysis error"}");
                else
                    Fail(row, counters, result.Error ?? "unknown analysis error");
                break;
        }
    }

    private void Skip(MemeIndexEntity row, MemeIndexRunCounters counters, string reason)
    {
        row.Status = MemeIndexStatus.Skipped;
        row.Error = reason;
        counters.Skipped++;
        logger.LogWarning("Meme attachment {AttachmentId} skipped: {Reason}", row.AttachmentDiscordId, reason);
    }

    // Transient failures (429/5xx, transport, download hiccups) must not burn one of the sweep's
    // capped attempts (#293): refund the increment from ProcessOneAsync so only deterministic
    // failures walk the row toward permanent abandonment. Status still flips to Failed so the
    // next sweep retries it.
    private void FailTransient(MemeIndexEntity row, MemeIndexRunCounters counters, string error)
    {
        row.AttemptCount--;
        Fail(row, counters, error);
    }

    private void Fail(MemeIndexEntity row, MemeIndexRunCounters counters, string error)
    {
        row.Status = MemeIndexStatus.Failed;
        row.Error = error;
        counters.Failed++;
        logger.LogWarning("Meme attachment {AttachmentId} failed (attempt {Attempt}): {Error}",
            row.AttachmentDiscordId, row.AttemptCount, error);
    }
}
