using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Jobs;

internal sealed class MemeIndexingJob(
    BackfillJobExecutor executor,
    IServiceScopeFactory scopeFactory,
    ILogger<MemeIndexingJob> logger) : BackfillJobBase
{
    public const int SweepMaxFailedAttempts = 3;

    // Refresh-urls accepts ~50 per call; chunking also keeps signed URLs
    // fresh relative to when they are actually downloaded.
    private const int UrlRefreshBatchSize = 50;

    protected override BackfillType BackfillType => BackfillType.MemeIndex;

    public Task ExecuteAsync(ulong guildId, CancellationToken cancellationToken)
        => RunAsync(guildId, maxFailedAttempts: null, cancellationToken);

    public Task ExecuteSweepAsync(ulong guildId, CancellationToken cancellationToken)
        => RunAsync(guildId, SweepMaxFailedAttempts, cancellationToken);

    public async Task IndexMessageAsync(ulong guildId, ulong messageDiscordId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var memeOptions = services.GetRequiredService<IOptions<MemeIndexOptions>>().Value;
        var openRouterOptions = services.GetRequiredService<IOptions<OpenRouterOptions>>().Value;

        // Config can change between enqueue and execution (deploy restart);
        // anything dropped here is healed by the weekly sweep.
        if (!memeOptions.IsConfigured || !openRouterOptions.IsConfigured
            || string.IsNullOrWhiteSpace(openRouterOptions.Model))
        {
            logger.LogWarning(
                "Live meme indexing for message {MessageId} in guild {GuildId} skipped: meme indexing is not fully configured",
                messageDiscordId, guildId);
            return;
        }

        var db = services.GetRequiredService<DiscordDbContext>();
        var sampleService = services.GetRequiredService<MemeSampleService>();
        var urlRefreshService = services.GetRequiredService<AttachmentUrlRefreshService>();
        var indexer = services.GetRequiredService<MemeAttachmentIndexer>();

        var candidates = (await sampleService.GetCandidatesForMessageAsync(messageDiscordId, cancellationToken))
            .OrderBy(c => c.AttachmentDiscordId)
            .ToList();

        var pending = await FilterTerminalAsync(db, candidates, cancellationToken);

        if (pending.Count == 0)
        {
            logger.LogDebug("Live meme indexing for message {MessageId}: nothing to do", messageDiscordId);
            return;
        }

        var freshUrls = await urlRefreshService.RefreshAsync(
            pending.Select(p => p.StoredUrl).ToList(), cancellationToken);

        var counters = new MemeIndexRunCounters();
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = await indexer.GetOrCreateRowAsync(db, item, cancellationToken);
            await indexer.ProcessOneAsync(db, row, item, freshUrls, counters, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Live meme indexing for message {MessageId} in guild {GuildId}: {Indexed} indexed, {Deduped} deduped, " +
            "{Skipped} skipped, {Failed} failed; {ModelCalls} model calls, cost {CostUsd:F4} USD",
            messageDiscordId, guildId, counters.Indexed, counters.Deduped, counters.Skipped, counters.Failed,
            counters.ModelCalls, counters.CostUsd);
    }

    private Task RunAsync(ulong guildId, int? maxFailedAttempts, CancellationToken cancellationToken)
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
            var indexer = ctx.Services.GetRequiredService<MemeAttachmentIndexer>();

            var allPending = await CollectPendingAsync(
                ctx.Db, sampleService, guildId, ctx.Checkpoint, maxFailedAttempts, cancellationToken);

            var cap = memeOptions.MaxImagesPerRun;
            var capped = allPending.Count > cap;
            var pending = allPending;
            if (capped)
            {
                logger.LogInformation(
                    "Meme indexing for guild {GuildId}: {Pending} attachments pending, capping this run at {Cap}",
                    guildId, allPending.Count, cap);
                pending = allPending.Take(cap).ToList();
            }

            // A fresh run (no resume cursor) restarts the per-run progress pair;
            // a mid-flight resume keeps accumulating into it, so the total must
            // include the prior progress or status shows processed > total.
            if (ctx.Checkpoint.LastProcessedId is null)
                ctx.Checkpoint.ProcessedCount = 0;
            ctx.Checkpoint.TotalCount = ctx.Checkpoint.ProcessedCount + pending.Count;
            await ctx.Db.SaveChangesAsync(cancellationToken);

            if (pending.Count == 0)
            {
                logger.LogInformation("Meme indexing for guild {GuildId}: nothing to do, all candidates terminal", guildId);
                return BackfillOutcome.Completed;
            }

            logger.LogInformation(
                "Meme indexing starting for guild {GuildId}: {Count} attachments, model {Model}",
                guildId, pending.Count, openRouterOptions.Model);

            // When the cap slices a multi-attachment message in half, the last capped
            // item is NOT the end of its message — the cursor must not advance past it.
            ulong? messageSplitByCap = capped && allPending[cap].MessageDiscordId == pending[^1].MessageDiscordId
                ? pending[^1].MessageDiscordId
                : null;

            var counters = new MemeIndexRunCounters();
            await ProcessPendingBatchesAsync(ctx, indexer, urlRefreshService, pending, messageSplitByCap, counters, cancellationToken);

            logger.LogInformation(
                "Meme indexing finished for guild {GuildId}: {Indexed} indexed, {Deduped} deduped, {Skipped} skipped, {Failed} failed; " +
                "{ModelCalls} model calls, tokens {PromptTokens}/{CompletionTokens}, cost {CostUsd:F4} USD{CapNote}",
                guildId, counters.Indexed, counters.Deduped, counters.Skipped, counters.Failed,
                counters.ModelCalls, counters.PromptTokens, counters.CompletionTokens, counters.CostUsd,
                capped ? " (cap reached — re-trigger to continue)" : "");

            return BackfillOutcome.Completed;
        }, cancellationToken);

    // All image attachments in this guild's meme channels that still need work:
    // no row yet, or a Failed row (retried, optionally attempt-capped for the
    // sweep). Terminal rows (Indexed/Skipped) are skipped — this is what makes
    // re-runs idempotent. Deterministic order is what makes the message-id
    // resume cursor valid across runs.
    private static async Task<List<MemeSampleItem>> CollectPendingAsync(
        DiscordDbContext db,
        MemeSampleService sampleService,
        ulong guildId,
        BackfillCheckpointEntity checkpoint,
        int? maxFailedAttempts,
        CancellationToken cancellationToken)
    {
        var candidates = (await sampleService.GetCandidatesAsync(cancellationToken))
            .Where(c => c.GuildDiscordId == guildId)
            .OrderBy(c => c.MessageDiscordId)
            .ThenBy(c => c.AttachmentDiscordId)
            .ToList();

        var statusByAttachment = await db.MemeIndex
            .Where(m => m.GuildDiscordId == guildId)
            .Select(m => new { m.AttachmentDiscordId, m.Status, m.AttemptCount })
            .ToDictionaryAsync(m => m.AttachmentDiscordId, m => (m.Status, m.AttemptCount), cancellationToken);

        // Only Indexed/Skipped are terminal. Failed retries by design; Pending
        // means a prior run was interrupted mid-attachment and the executor's
        // failure path flushed the freshly-added row — it must be picked up
        // again or the attachment is silently lost from the index.
        var pending = candidates
            .Where(c =>
            {
                if (!statusByAttachment.TryGetValue(c.AttachmentDiscordId, out var row))
                    return true;
                return row.Status switch
                {
                    MemeIndexStatus.Pending => true,
                    MemeIndexStatus.Failed => maxFailedAttempts is not { } capAttempts
                        || row.AttemptCount < capAttempts,
                    _ => false
                };
            })
            .ToList();

        // Resume cursor survives only a mid-flight interruption (the executor
        // clears it after terminal runs); skip messages already fully processed.
        if (checkpoint.LastProcessedId is { } cursor)
            pending = pending.Where(c => c.MessageDiscordId > cursor).ToList();

        return pending;
    }

    // Terminal rows stay untouched — relevant when Hangfire retries this
    // job after a crash that landed between attachments.
    private static async Task<List<MemeSampleItem>> FilterTerminalAsync(
        DiscordDbContext db, List<MemeSampleItem> candidates, CancellationToken cancellationToken)
    {
        var attachmentIds = candidates.Select(c => c.AttachmentDiscordId).ToList();
        var terminal = await db.MemeIndex
            .Where(m => attachmentIds.Contains(m.AttachmentDiscordId)
                        && (m.Status == MemeIndexStatus.Indexed || m.Status == MemeIndexStatus.Skipped))
            .Select(m => m.AttachmentDiscordId)
            .ToListAsync(cancellationToken);
        return candidates.Where(c => !terminal.Contains(c.AttachmentDiscordId)).ToList();
    }

    private async Task ProcessPendingBatchesAsync(
        BackfillContext ctx,
        MemeAttachmentIndexer indexer,
        AttachmentUrlRefreshService urlRefreshService,
        List<MemeSampleItem> pending,
        ulong? messageSplitByCap,
        MemeIndexRunCounters counters,
        CancellationToken cancellationToken)
    {
        var position = 0;
        foreach (var batch in pending.Chunk(UrlRefreshBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var freshUrls = await urlRefreshService.RefreshAsync(
                batch.Select(b => b.StoredUrl).ToList(), cancellationToken);

            foreach (var item in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = await indexer.GetOrCreateRowAsync(ctx.Db, item, cancellationToken);
                await indexer.ProcessOneAsync(ctx.Db, row, item, freshUrls, counters, cancellationToken);

                position++;
                ctx.Checkpoint.ProcessedCount++;
                // Advance the resume cursor only once a message's LAST pending
                // attachment is done — a mid-message cursor would skip siblings.
                var lastOfMessage = position == pending.Count
                    ? item.MessageDiscordId != messageSplitByCap
                    : pending[position].MessageDiscordId != item.MessageDiscordId;
                if (lastOfMessage)
                    ctx.Checkpoint.LastProcessedId = item.MessageDiscordId;

                await SaveProgressAsync(ctx.Db, ctx.Checkpoint, cancellationToken);
            }
        }
    }
}
