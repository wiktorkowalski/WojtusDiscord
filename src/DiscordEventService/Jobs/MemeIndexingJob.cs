using System.Security.Cryptography;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Jobs;

// #221: walks every image attachment in the configured meme channels and
// produces Indexed meme rows. Purely DB-driven (binding comment on #221):
// candidates come from messages.attachments_json, expired CDN URLs are
// re-signed via attachments/refresh-urls — no channel-history pagination, no
// gateway. Idempotent: terminal rows (Indexed/Skipped) are skipped on re-run,
// Failed rows are retried. Not part of the weekly full-backfill chain.
public sealed class MemeIndexingJob(
    BackfillJobExecutor executor,
    IHttpClientFactory httpClientFactory,
    ILogger<MemeIndexingJob> logger) : BackfillJobBase
{
    protected override BackfillType BackfillType => BackfillType.MemeIndex;

    private sealed class RunCounters
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

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => executor.RunAsync(BackfillType, guildId, async ctx =>
        {
            var memeOptions = ctx.Services.GetRequiredService<IOptions<MemeIndexOptions>>().Value;
            var openRouterOptions = ctx.Services.GetRequiredService<IOptions<OpenRouterOptions>>().Value;

            if (!memeOptions.IsConfigured)
                return BackfillOutcome.ShortCircuit("MemeIndex:ChannelIds is empty — no meme channels configured");
            if (!openRouterOptions.IsConfigured)
                return BackfillOutcome.ShortCircuit("OpenRouter:ApiKey is not configured");
            if (string.IsNullOrWhiteSpace(openRouterOptions.Model))
                return BackfillOutcome.ShortCircuit("OpenRouter:Model is not set — the #219 model pick is required");

            var sampleService = ctx.Services.GetRequiredService<MemeSampleService>();
            var urlRefreshService = ctx.Services.GetRequiredService<AttachmentUrlRefreshService>();
            var openRouterClient = ctx.Services.GetRequiredService<OpenRouterClient>();

            var pending = await CollectPendingAsync(ctx.Db, sampleService, guildId, ctx.Checkpoint, cancellationToken);

            var cap = memeOptions.MaxImagesPerRun;
            var capped = pending.Count > cap;
            if (capped)
            {
                logger.LogInformation(
                    "Meme indexing for guild {GuildId}: {Pending} attachments pending, capping this run at {Cap}",
                    guildId, pending.Count, cap);
                pending = pending.Take(cap).ToList();
            }

            // A fresh run (no resume cursor) restarts the per-run progress pair;
            // a mid-flight resume keeps accumulating into it.
            if (ctx.Checkpoint.LastProcessedId is null)
                ctx.Checkpoint.ProcessedCount = 0;
            ctx.Checkpoint.TotalCount = pending.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            if (pending.Count == 0)
            {
                logger.LogInformation("Meme indexing for guild {GuildId}: nothing to do, all candidates terminal", guildId);
                return BackfillOutcome.Completed;
            }

            logger.LogInformation(
                "Meme indexing starting for guild {GuildId}: {Count} attachments, model {Model}",
                guildId, pending.Count, openRouterOptions.Model);

            var counters = new RunCounters();

            // Refresh-urls accepts ~50 per call; chunking also keeps signed URLs
            // fresh relative to when they're actually downloaded.
            var position = 0;
            foreach (var batch in pending.Chunk(50))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var freshUrls = await urlRefreshService.RefreshAsync(
                    batch.Select(b => b.StoredUrl).ToList(), cancellationToken);

                foreach (var item in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var row = await GetOrCreateRowAsync(ctx.Db, item, cancellationToken);
                    await ProcessOneAsync(ctx.Db, row, item, freshUrls, openRouterClient, memeOptions, openRouterOptions, counters, cancellationToken);

                    position++;
                    ctx.Checkpoint.ProcessedCount++;
                    // Advance the resume cursor only once a message's LAST pending
                    // attachment is done — a mid-message cursor would skip siblings.
                    var lastOfMessage = position == pending.Count
                        || pending[position].MessageDiscordId != item.MessageDiscordId;
                    if (lastOfMessage)
                        ctx.Checkpoint.LastProcessedId = item.MessageDiscordId;

                    await SaveProgressAsync(ctx.Db, ctx.Checkpoint);
                }
            }

            logger.LogInformation(
                "Meme indexing finished for guild {GuildId}: {Indexed} indexed, {Deduped} deduped, {Skipped} skipped, {Failed} failed; " +
                "{ModelCalls} model calls, tokens {PromptTokens}/{CompletionTokens}, cost {CostUsd:F4} USD{CapNote}",
                guildId, counters.Indexed, counters.Deduped, counters.Skipped, counters.Failed,
                counters.ModelCalls, counters.PromptTokens, counters.CompletionTokens, counters.CostUsd,
                capped ? " (cap reached — re-trigger to continue)" : "");

            return BackfillOutcome.Completed;
        }, cancellationToken);

    // All image attachments in this guild's meme channels that still need work:
    // no row yet, or a Failed row (retried). Terminal rows (Indexed/Skipped) are
    // skipped — this is what makes re-runs idempotent. Deterministic order is
    // what makes the message-id resume cursor valid across runs.
    private static async Task<List<MemeSampleItem>> CollectPendingAsync(
        DiscordDbContext db,
        MemeSampleService sampleService,
        ulong guildId,
        BackfillCheckpointEntity checkpoint,
        CancellationToken cancellationToken)
    {
        var candidates = (await sampleService.GetCandidatesAsync(cancellationToken))
            .Where(c => c.GuildDiscordId == guildId)
            .OrderBy(c => c.MessageDiscordId)
            .ThenBy(c => c.AttachmentDiscordId)
            .ToList();

        var statusByAttachment = await db.MemeIndex
            .Where(m => m.GuildDiscordId == guildId)
            .Select(m => new { m.AttachmentDiscordId, m.Status })
            .ToDictionaryAsync(m => m.AttachmentDiscordId, m => m.Status, cancellationToken);

        var pending = candidates
            .Where(c => !statusByAttachment.TryGetValue(c.AttachmentDiscordId, out var status)
                        || status == MemeIndexStatus.Failed)
            .ToList();

        // Resume cursor survives only a mid-flight interruption (the executor
        // clears it after terminal runs); skip messages already fully processed.
        if (checkpoint.LastProcessedId is { } cursor)
            pending = pending.Where(c => c.MessageDiscordId > cursor).ToList();

        return pending;
    }

    private static async Task<MemeIndexEntity> GetOrCreateRowAsync(
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
            Status = MemeIndexStatus.Pending
        };
        db.MemeIndex.Add(row);
        return row;
    }

    private async Task ProcessOneAsync(
        DiscordDbContext db,
        MemeIndexEntity row,
        MemeSampleItem item,
        Dictionary<string, string> freshUrls,
        OpenRouterClient openRouterClient,
        MemeIndexOptions memeOptions,
        OpenRouterOptions openRouterOptions,
        RunCounters counters,
        CancellationToken cancellationToken)
    {
        row.AttemptCount++;

        if (!freshUrls.TryGetValue(AttachmentUrlRefreshService.StripQuery(item.StoredUrl), out var freshUrl))
        {
            Skip(row, counters, "dead attachment: refresh-urls declined to re-sign");
            return;
        }

        byte[] imageBytes;
        try
        {
            var client = httpClientFactory.CreateClient(MemeBenchmarkJob.DownloadHttpClientName);
            imageBytes = await client.GetByteArrayAsync(freshUrl, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Gone)
        {
            Skip(row, counters, $"dead attachment: download HTTP {(int)ex.StatusCode!}");
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Fail(row, counters, $"download failed: {ex.Message}");
            return;
        }

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

        // Repost dedupe: same bytes already Indexed → copy its metadata, no
        // model call. RawResponseJson stays null — provenance lives on the
        // original row, found via the shared content_hash.
        var original = await db.MemeIndex.AsNoTracking()
            .Where(m => m.ContentHash == contentHash && m.Status == MemeIndexStatus.Indexed && m.Id != row.Id)
            .Select(m => new { m.DescriptionPl, m.DescriptionEn, m.OcrText, m.Tags, m.Source, m.Template, m.ModelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (original is not null)
        {
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
            return;
        }

        var result = await openRouterClient.AnalyzeImageAsync(imageBytes, mimeType, openRouterOptions.Model, cancellationToken);
        counters.ModelCalls++;
        counters.PromptTokens += result.Usage?.PromptTokens ?? 0;
        counters.CompletionTokens += result.Usage?.CompletionTokens ?? 0;
        counters.CostUsd += result.Usage?.CostUsd ?? 0;

        switch (result.Outcome)
        {
            case MemeAnalysisOutcome.Success:
                row.DescriptionPl = result.Metadata!.DescriptionPl;
                row.DescriptionEn = result.Metadata.DescriptionEn;
                row.OcrText = result.Metadata.OcrText;
                row.Tags = result.Metadata.Tags;
                row.Source = result.Metadata.Source;
                row.Template = result.Metadata.Template;
                row.ModelId = openRouterOptions.Model;
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
                Fail(row, counters, result.Error ?? "unknown analysis error");
                break;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(openRouterOptions.RequestDelayMs), cancellationToken);
    }

    private void Skip(MemeIndexEntity row, RunCounters counters, string reason)
    {
        row.Status = MemeIndexStatus.Skipped;
        row.Error = reason;
        counters.Skipped++;
        logger.LogInformation("Meme attachment {AttachmentId} skipped: {Reason}", row.AttachmentDiscordId, reason);
    }

    private void Fail(MemeIndexEntity row, RunCounters counters, string error)
    {
        row.Status = MemeIndexStatus.Failed;
        row.Error = error;
        counters.Failed++;
        logger.LogWarning("Meme attachment {AttachmentId} failed (attempt {Attempt}): {Error}",
            row.AttachmentDiscordId, row.AttemptCount, error);
    }
}
