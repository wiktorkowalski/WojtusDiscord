using System.Security.Cryptography;
using System.Text.Json;
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

internal enum MemeAnnotationWrite
{
    Added,
    Overwritten,

    // The key already holds the same metadata: nothing was written.
    Unchanged,
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
        var copied = CopyMissingAnnotations(db, row, originals, existingKeys);

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

    private int CopyMissingAnnotations(
        DiscordDbContext db, MemeIndexEntity row, List<MemeAnnotationEntity> originals, HashSet<AnnotationKey> existingKeys)
    {
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
                ImageKind = original.ImageKind,
                Templates = original.Templates,
                People = JsonSerializer.Deserialize<MemePerson[]>(original.People) ?? [],
                SearchPhrases = original.SearchPhrases,
                Franchise = original.Franchise,
                Source = original.Source,
                Language = original.Language,
            };
            WriteAnnotation(db, row, existing: null, original.ModelId, original.PromptVersion, original.ReasoningEffort,
                metadata, rawResponseJson: null, copiedAtUtc);
            copied++;
        }

        return copied;
    }

    // The import's entry (#369): the same write as the model path, an overwrite when the key
    // already holds an annotation, and the status flip in the same tracked unit.
    public async Task<MemeAnnotationWrite> ImportAnnotationAsync(
        DiscordDbContext db,
        MemeIndexEntity row,
        string modelId,
        string promptVersion,
        string? reasoningEffort,
        MemeMetadata metadata,
        DateTime indexedAtUtc,
        CancellationToken cancellationToken)
    {
        // A row not saved yet cannot have annotations.
        var existing = db.Entry(row).State == EntityState.Added
            ? null
            : await db.MemeAnnotations.FirstOrDefaultAsync(
                a => a.AttachmentDiscordId == row.AttachmentDiscordId && a.ModelId == modelId && a.PromptVersion == promptVersion,
                cancellationToken);

        // The raw column gets the parsed contract, not the text as sent: an external writer has no
        // strict schema, and a member outside the contract could carry a name past the cut-out rule.
        var write = WriteAnnotation(db, row, existing, modelId, promptVersion, reasoningEffort,
            metadata, JsonSerializer.Serialize(metadata), indexedAtUtc);

        // A Failed or Skipped row becomes findable too: search needs Indexed, and it has an annotation now.
        MarkIndexed(row);
        return write;
    }

    // The one place an annotation is written: the model path, the repost copy and the import all
    // end here, so the cut-out rule (#368) holds for every writer. Only the import passes
    // `existing`; the other two check the key first and never write one key twice.
    private MemeAnnotationWrite WriteAnnotation(
        DiscordDbContext db,
        MemeIndexEntity row,
        MemeAnnotationEntity? existing,
        string modelId,
        string promptVersion,
        string? reasoningEffort,
        MemeMetadata metadata,
        string? rawResponseJson,
        DateTime indexedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
            reasoningEffort = null;

        var stored = MemeMetadataSanitizer.Sanitize(metadata);
        var franchise = string.IsNullOrWhiteSpace(stored.Franchise) ? null : stored.Franchise;
        if (existing is not null && HasSameContent(existing, stored, franchise, reasoningEffort))
            return MemeAnnotationWrite.Unchanged;

        if (!ReferenceEquals(stored, metadata))
        {
            // wojtus_query reads every table: a name the rule dropped must not survive in the raw column.
            if (rawResponseJson is not null)
                rawResponseJson = JsonSerializer.Serialize(stored);

            LogCutoutRule(row, modelId, promptVersion, metadata, stored);
        }

        var annotation = existing ?? new MemeAnnotationEntity
        {
            MemeIndex = row,
            AttachmentDiscordId = row.AttachmentDiscordId,
            ModelId = modelId,
            PromptVersion = promptVersion,
        };
        annotation.ReasoningEffort = reasoningEffort;
        annotation.IndexedAtUtc = indexedAtUtc;
        annotation.DescriptionPl = stored.DescriptionPl;
        annotation.DescriptionEn = stored.DescriptionEn;
        annotation.OcrText = stored.OcrText;
        annotation.Tags = stored.Tags;
        annotation.Templates = stored.Templates;
        annotation.SearchPhrases = stored.SearchPhrases;
        annotation.People = JsonSerializer.Serialize(stored.People);
        annotation.PeopleNames = [.. stored.People.Select(p => p.Name)];
        annotation.ImageKind = stored.ImageKind;
        annotation.Language = stored.Language;
        annotation.Franchise = franchise;
        annotation.Source = stored.Source;
        annotation.RawResponseJson = rawResponseJson;

        if (existing is not null)
            return MemeAnnotationWrite.Overwritten;

        db.MemeAnnotations.Add(annotation);
        return MemeAnnotationWrite.Added;
    }

    // Counts only. A name in the log would undo the rule.
    private void LogCutoutRule(
        MemeIndexEntity row, string modelId, string promptVersion, MemeMetadata metadata, MemeMetadata stored)
    {
        var droppedTerms = metadata.Tags.Length - stored.Tags.Length
            + metadata.Templates.Length - stored.Templates.Length
            + metadata.SearchPhrases.Length - stored.SearchPhrases.Length
            + (metadata.Franchise is not null && stored.Franchise is null ? 1 : 0);
        logger.LogInformation(
            "Cut-out rule applied to meme attachment {AttachmentId} ({Model} {PromptVersion}): dropped {DroppedPeople} people and {DroppedTerms} terms that name them",
            row.AttachmentDiscordId, modelId, promptVersion, metadata.People.Length, droppedTerms);
    }

    // The raw column and indexed_at_utc are left out: the same metadata sent again is the same
    // annotation, whenever it is sent.
    private static bool HasSameContent(
        MemeAnnotationEntity existing, MemeMetadata stored, string? franchise, string? reasoningEffort) =>
        existing.ReasoningEffort == reasoningEffort
        && existing.DescriptionPl == stored.DescriptionPl
        && existing.DescriptionEn == stored.DescriptionEn
        && existing.OcrText == stored.OcrText
        && existing.Tags.SequenceEqual(stored.Tags)
        && existing.Templates.SequenceEqual(stored.Templates)
        && existing.SearchPhrases.SequenceEqual(stored.SearchPhrases)
        && HasSamePeople(existing.People, stored.People)
        && existing.ImageKind == stored.ImageKind
        && existing.Language == stored.Language
        && existing.Franchise == franchise
        && existing.Source == stored.Source;

    // Unreadable stored people count as different: the overwrite then repairs the row.
    private static bool HasSamePeople(string storedPeopleJson, MemePerson[] people)
    {
        try
        {
            return (JsonSerializer.Deserialize<MemePerson[]>(storedPeopleJson) ?? []).SequenceEqual(people);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
            case MemeAnalysisOutcome.Success when HasNullRequiredField(result.Metadata!):
                Fail(row, counters, "model returned null for a required metadata field");
                break;

            // The annotation and the status flip stay in one tracked unit: the caller's single
            // SaveChanges is the only thing that keeps "Indexed has an annotation" true.
            case MemeAnalysisOutcome.Success:
                WriteAnnotation(db, row, existing: null, openRouter.Model, OpenRouterClient.PromptVersion,
                    openRouter.ReasoningEffort, result.Metadata!, result.RawContent, DateTime.UtcNow);
                MarkIndexed(row);
                counters.Indexed++;
                break;

            // A refusal is this writer's outcome, not the attachment's (#373): the marker is what
            // lets the manual backfill offer the image to another model, and not to this one again.
            // The reason names the writer too: it is the text of both Warning lines in Skip.
            case MemeAnalysisOutcome.Refusal:
                Skip(row, counters, $"model refusal by {openRouter.Model} {OpenRouterClient.PromptVersion}: {result.Error}",
                    refusedBy: new AnnotationKey(openRouter.Model, OpenRouterClient.PromptVersion));
                break;

            default:
                if (result.IsTransient)
                    FailTransient(row, counters, $"transient: {result.Error ?? "unknown analysis error"}");
                else
                    Fail(row, counters, result.Error ?? "unknown analysis error");
                break;
        }
    }

    // `required` in System.Text.Json is presence-only: an explicit null passes deserialization
    // but violates the NOT NULL metadata columns at save time, which would poison the run (#311).
    // franchise and source are the only members the contract lets be null.
    public static bool HasNullRequiredField(MemeMetadata metadata) =>
        metadata.DescriptionPl is null || metadata.DescriptionEn is null || metadata.OcrText is null
        || metadata.ImageKind is null || metadata.Language is null
        || HasNull(metadata.Tags) || HasNull(metadata.Templates) || HasNull(metadata.SearchPhrases)
        || metadata.People is null || metadata.People.Any(p => p?.Name is null);

    private static bool HasNull(string[]? values) => values is null || values.Any(v => v is null);

    // refusedBy = the writer that refused the image; null = an attachment-level skip.
    private void Skip(MemeIndexEntity row, MemeIndexRunCounters counters, string reason, AnnotationKey? refusedBy = null)
    {
        counters.Skipped++;

        // A refusal marks the row whatever its status. An attachment-level skip is terminal for
        // every writer, so it takes an earlier marker away: with it the manual backfill would
        // keep coming back (#373). An Indexed row is not downgraded and keeps its marker.
        if (refusedBy is not null || row.Status != MemeIndexStatus.Indexed)
            (row.RefusedByModelId, row.RefusedByPromptVersion) = (refusedBy?.ModelId, refusedBy?.PromptVersion);

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
