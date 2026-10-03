using System.Security.Cryptography;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DiscordEventService.Services.MemeIndexing;

internal sealed class MemeIndexRunCounters
{
    public int Indexed { get; set; }
    public int Deduped { get; set; }
    public int AlreadyAnnotated { get; set; }
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
    private const int PoisonErrorMaxLength = 500;

    private readonly record struct AnnotationKey(string ModelId, string PromptVersion);

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

        // Idempotent per annotation key (#367): the configured writer's annotation is already
        // there (an import, or a status row that never reached Indexed). Checked before any
        // download or model call — a second INSERT of one key is a unique violation, not an update.
        var existingKeys = await LoadAnnotationKeysAsync(db, row, cancellationToken);
        var configuredKey = new AnnotationKey(openRouter.Model, OpenRouterClient.PromptVersion);
        if (existingKeys.Contains(configuredKey))
        {
            MarkIndexed(row);
            counters.AlreadyAnnotated++;
            logger.LogDebug("Meme attachment {AttachmentId} already has the {Model} {PromptVersion} annotation",
                row.AttachmentDiscordId, configuredKey.ModelId, configuredKey.PromptVersion);
            return;
        }

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

        // An Indexed row is here only to gain one more annotation; it has no attempt budget.
        if (row.Status != MemeIndexStatus.Indexed)
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

        if (await TryDedupeByContentHashAsync(db, row, contentHash, existingKeys, configuredKey, counters, cancellationToken))
            return;

        var result = await openRouterClient.AnalyzeImageAsync(
            imageBytes, mimeType, openRouter.Model, openRouter.ReasoningEffort, cancellationToken);
        counters.ModelCalls++;
        counters.PromptTokens += result.Usage?.PromptTokens ?? 0;
        counters.CompletionTokens += result.Usage?.CompletionTokens ?? 0;
        counters.CostUsd += result.Usage?.CostUsd ?? 0;

        ApplyAnalysisResult(db, row, result, openRouter, counters);

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

    // A row not saved yet cannot have annotations; skip the round-trip for it.
    private static async Task<HashSet<AnnotationKey>> LoadAnnotationKeysAsync(
        DiscordDbContext db, MemeIndexEntity row, CancellationToken cancellationToken)
    {
        if (db.Entry(row).State == EntityState.Added)
            return [];

        var keys = await db.MemeAnnotations.AsNoTracking()
            .Where(a => a.MemeIndexId == row.Id)
            .Select(a => new { a.ModelId, a.PromptVersion })
            .ToListAsync(cancellationToken);
        return [.. keys.Select(k => new AnnotationKey(k.ModelId, k.PromptVersion))];
    }

    // Repost dedupe: the same bytes are already Indexed on another attachment → copy every
    // annotation of the oldest such row that this one lacks, no model call. No propagation: a
    // later annotation of the original reaches this row only when the manual backfill revisits it.
    // Copies carry no raw response — provenance stays on the original, found via content_hash.
    private async Task<bool> TryDedupeByContentHashAsync(
        DiscordDbContext db,
        MemeIndexEntity row,
        string contentHash,
        HashSet<AnnotationKey> existingKeys,
        AnnotationKey configuredKey,
        MemeIndexRunCounters counters,
        CancellationToken cancellationToken)
    {
        // Oldest first: two Indexed rows with one hash can hold different annotation sets.
        var originalId = await db.MemeIndex.AsNoTracking()
            .Where(m => m.ContentHash == contentHash && m.Status == MemeIndexStatus.Indexed && m.Id != row.Id
                        && m.Annotations.Any())
            .OrderBy(m => m.FirstSeenUtc)
            .ThenBy(m => m.Id)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (originalId is null) return false;

        var originals = await db.MemeAnnotations.AsNoTracking()
            .Where(a => a.MemeIndexId == originalId)
            .OrderBy(a => a.IndexedAtUtc)
            .ToListAsync(cancellationToken);

        var wasIndexed = row.Status == MemeIndexStatus.Indexed;
        var copiedAtUtc = DateTime.UtcNow;
        var copied = 0;
        foreach (var original in originals)
        {
            if (!existingKeys.Add(new AnnotationKey(original.ModelId, original.PromptVersion)))
                continue;

            var metadata = new MemeMetadata
            {
                DescriptionPl = original.DescriptionPl,
                DescriptionEn = original.DescriptionEn,
                OcrText = original.OcrText,
                Tags = original.Tags,
                Source = original.Source,
                Template = original.Template,
            };
            AddAnnotation(db, row, original.ModelId, original.PromptVersion, original.ReasoningEffort,
                metadata, rawResponseJson: null, copiedAtUtc);
            copied++;
        }

        // An already Indexed row is here for the configured writer's annotation (manual backfill).
        // When the original lacks it too, only the model can supply it; the copies still save.
        if (copied == 0 || (wasIndexed && !existingKeys.Contains(configuredKey)))
        {
            if (copied > 0)
                logger.LogDebug(
                    "Meme attachment {AttachmentId}: {Copied} annotations copied via content hash {ContentHash}; the configured writer's annotation still needs the model",
                    row.AttachmentDiscordId, copied, contentHash);
            return false;
        }

        MarkIndexed(row);
        counters.Deduped++;
        logger.LogDebug("Meme attachment {AttachmentId} deduped via content hash {ContentHash}: {Copied} annotations copied",
            row.AttachmentDiscordId, contentHash, copied);
        return true;
    }

    // The one place an annotation is written: the model path and the repost copy both end here.
    private static void AddAnnotation(
        DiscordDbContext db,
        MemeIndexEntity row,
        string modelId,
        string promptVersion,
        string? reasoningEffort,
        MemeMetadata metadata,
        string? rawResponseJson,
        DateTime indexedAtUtc) =>
        db.MemeAnnotations.Add(new MemeAnnotationEntity
        {
            MemeIndex = row,
            AttachmentDiscordId = row.AttachmentDiscordId,
            ModelId = modelId,
            PromptVersion = promptVersion,
            ReasoningEffort = reasoningEffort,
            IndexedAtUtc = indexedAtUtc,
            DescriptionPl = metadata.DescriptionPl,
            DescriptionEn = metadata.DescriptionEn,
            OcrText = metadata.OcrText,
            Tags = metadata.Tags,
            Source = metadata.Source,
            Template = metadata.Template,
            RawResponseJson = rawResponseJson,
        });

    private static void MarkIndexed(MemeIndexEntity row)
    {
        row.Status = MemeIndexStatus.Indexed;
        row.Error = null;
    }

    private void ApplyAnalysisResult(
        DiscordDbContext db, MemeIndexEntity row, MemeAnalysisResult result, OpenRouterOptions openRouter, MemeIndexRunCounters counters)
    {
        switch (result.Outcome)
        {
            // `required` in System.Text.Json is presence-only: an explicit null passes deserialization
            // but violates the NOT NULL metadata columns at save time, which would poison the run (#311).
            case MemeAnalysisOutcome.Success when result.Metadata is { DescriptionPl: null } or { DescriptionEn: null } or { OcrText: null } or { Tags: null }:
                Fail(row, counters, "model returned null for a required metadata field");
                break;

            // The annotation and the status flip stay in one tracked unit: the caller's single
            // SaveChanges is the only thing that keeps "Indexed has an annotation" true.
            case MemeAnalysisOutcome.Success:
                AddAnnotation(db, row, openRouter.Model, OpenRouterClient.PromptVersion,
                    string.IsNullOrEmpty(openRouter.ReasoningEffort) ? null : openRouter.ReasoningEffort,
                    result.Metadata!, result.RawContent, DateTime.UtcNow);
                MarkIndexed(row);
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
        counters.Skipped++;
        if (StaysIndexed(row, reason))
            return;

        row.Status = MemeIndexStatus.Skipped;
        row.Error = reason;
        logger.LogWarning("Meme attachment {AttachmentId} skipped: {Reason}", row.AttachmentDiscordId, reason);
    }

    // An Indexed row is revisited only to gain one more annotation (manual backfill, #367).
    // People already find it through the annotations it has: a failed extra never takes it out of search.
    private bool StaysIndexed(MemeIndexEntity row, string outcome)
    {
        if (row.Status != MemeIndexStatus.Indexed)
            return false;

        logger.LogWarning("Meme attachment {AttachmentId} stays Indexed; the configured writer's annotation was not written: {Outcome}",
            row.AttachmentDiscordId, outcome);
        return true;
    }

    // Transient failures (429/5xx, transport, download hiccups) must not burn one of the sweep's
    // capped attempts (#293): refund the increment from ProcessOneAsync so only deterministic
    // failures walk the row toward permanent abandonment. Status still flips to Failed so the
    // next sweep retries it.
    private void FailTransient(MemeIndexEntity row, MemeIndexRunCounters counters, string error)
    {
        // Mirrors the increment: an Indexed row was never charged.
        if (row.Status != MemeIndexStatus.Indexed)
            row.AttemptCount--;
        Fail(row, counters, error);
    }

    // Only SQLSTATE 22/23 (the data itself was refused) is deterministic and charges an attempt;
    // timeouts and dropped connections must not burn the sweep's cap (#293). No refund: the row was
    // reloaded after the failed save, so ProcessOneAsync's increment is already gone.
    public void FailRejectedSave(MemeIndexEntity row, MemeIndexRunCounters counters, Exception ex)
    {
        var detail = $"{ex.GetType().Name}: {ex.GetBaseException().Message}";
        if (detail.Length > PoisonErrorMaxLength)
            detail = detail[..PoisonErrorMaxLength];

        if (IsDataRejection(ex))
        {
            row.AttemptCount++;
            Fail(row, counters, $"poisoned: {detail}");
        }
        else
        {
            Fail(row, counters, $"transient: save failed: {detail}");
        }
    }

    private static bool IsDataRejection(Exception ex) =>
        ex.GetBaseException() is PostgresException pg
        && (pg.SqlState.StartsWith("22", StringComparison.Ordinal) || pg.SqlState.StartsWith("23", StringComparison.Ordinal));

    private void Fail(MemeIndexEntity row, MemeIndexRunCounters counters, string error)
    {
        counters.Failed++;
        if (StaysIndexed(row, error))
            return;

        row.Status = MemeIndexStatus.Failed;
        row.Error = error;
        logger.LogWarning("Meme attachment {AttachmentId} failed (attempt {Attempt}): {Error}",
            row.AttachmentDiscordId, row.AttemptCount, error);
    }
}
