using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordEventService.Data;
using DiscordEventService.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Services.MemeIndexing;

// One element of the import payload (#369). `metadata` is the MemeMetadata contract, the same
// JSON a model returns through the API.
internal sealed record MemeAnnotationImportItem
{
    // Read from a string too: a snowflake is above 2^53, and jq / JavaScript round it as a number.
    [JsonPropertyName("attachment_discord_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public required ulong AttachmentDiscordId { get; init; }

    [JsonPropertyName("model_id")]
    public required string ModelId { get; init; }

    [JsonPropertyName("prompt_version")]
    public required string PromptVersion { get; init; }

    // When the annotation was produced. Left out = the time of the import.
    [JsonPropertyName("indexed_at_utc")]
    public DateTime? IndexedAtUtc { get; init; }

    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; init; }

    [JsonPropertyName("metadata")]
    public required JsonElement Metadata { get; init; }
}

internal static class MemeAnnotationImportOutcome
{
    public const string Imported = "imported";
    public const string Skipped = "skipped";
    public const string Rejected = "rejected";
}

internal sealed record MemeAnnotationImportItemResult
{
    // Position in the request array.
    public required int Index { get; init; }

    // A string, like every snowflake that leaves the service. Null when the item did not parse.
    public string? AttachmentDiscordId { get; init; }

    public required string Outcome { get; init; }

    // True when an annotation with this key existed and its metadata was replaced.
    public bool Overwritten { get; init; }

    public string? Reason { get; init; }
}

internal sealed record MemeAnnotationImportResponse
{
    public required int Imported { get; init; }

    // How many of Imported replaced an existing annotation.
    public required int Overwritten { get; init; }
    public required int Skipped { get; init; }
    public required int Rejected { get; init; }
    public required List<MemeAnnotationImportItemResult> Items { get; init; }
}

// Writes externally produced annotations (#369). Each item is validated like API output and is
// written through MemeAttachmentIndexer, so the cut-out rule (#368) holds here too.
internal sealed class MemeAnnotationImportService(
    DiscordDbContext db,
    MemeSampleService sampleService,
    MemeAttachmentIndexer indexer,
    ILogger<MemeAnnotationImportService> logger)
{
    private const int SaveErrorMaxLength = 500;

    private readonly record struct ImportKey(ulong AttachmentDiscordId, string ModelId, string PromptVersion);

    public async Task<MemeAnnotationImportResponse> ImportAsync(
        IReadOnlyList<JsonElement> items, CancellationToken cancellationToken)
    {
        // The one place that knows which attachments are images in a configured meme channel.
        var candidates = (await sampleService.GetCandidatesAsync(cancellationToken))
            .DistinctBy(c => c.AttachmentDiscordId)
            .ToDictionary(c => c.AttachmentDiscordId);

        var importedAtUtc = DateTime.UtcNow;
        var seenKeys = new HashSet<ImportKey>();
        var results = new List<MemeAnnotationImportItemResult>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ImportOneAsync(index, items[index], candidates, seenKeys, importedAtUtc, cancellationToken);
            results.Add(result);

            if (result.Outcome == MemeAnnotationImportOutcome.Rejected)
                logger.LogDebug("Meme annotation import item {Index} (attachment {AttachmentId}) rejected: {Reason}",
                    index, result.AttachmentDiscordId, result.Reason);
        }

        var response = new MemeAnnotationImportResponse
        {
            Imported = results.Count(r => r.Outcome == MemeAnnotationImportOutcome.Imported),
            Overwritten = results.Count(r => r.Overwritten),
            Skipped = results.Count(r => r.Outcome == MemeAnnotationImportOutcome.Skipped),
            Rejected = results.Count(r => r.Outcome == MemeAnnotationImportOutcome.Rejected),
            Items = results,
        };

        logger.LogInformation(
            "Meme annotation import: {Items} items, {Imported} imported ({Overwritten} overwritten), {Skipped} skipped, {Rejected} rejected",
            items.Count, response.Imported, response.Overwritten, response.Skipped, response.Rejected);

        return response;
    }

    private async Task<MemeAnnotationImportItemResult> ImportOneAsync(
        int index,
        JsonElement element,
        Dictionary<ulong, MemeSampleItem> candidates,
        HashSet<ImportKey> seenKeys,
        DateTime importedAtUtc,
        CancellationToken cancellationToken)
    {
        MemeAnnotationImportItem? item;
        try
        {
            item = element.Deserialize<MemeAnnotationImportItem>();
        }
        catch (JsonException ex)
        {
            return Rejected(index, attachmentId: null, $"invalid item: {ex.Message}");
        }

        if (item is null)
            return Rejected(index, attachmentId: null, "invalid item: null");

        // Cheap checks first; nothing before the write touches the database.
        var attachmentId = item.AttachmentDiscordId;
        if (ValidateKey(item) is { } keyReason)
            return Rejected(index, attachmentId, keyReason);
        if (ParseMetadata(item.Metadata, out var metadata) is { } schemaReason)
            return Rejected(index, attachmentId, schemaReason);
        if (!candidates.TryGetValue(attachmentId, out var sample))
            return Rejected(index, attachmentId, "attachment is not an image in a configured meme channel");

        var key = new ImportKey(attachmentId, item.ModelId, item.PromptVersion);
        if (!seenKeys.Add(key))
            return Rejected(index, attachmentId, "duplicate key in this batch: an earlier item has the same attachment, model_id and prompt_version");

        try
        {
            var write = await WriteAsync(item, metadata!, sample, importedAtUtc, cancellationToken);
            return write == MemeAnnotationWrite.Unchanged
                ? Result(index, attachmentId, MemeAnnotationImportOutcome.Skipped, "the same metadata is already stored under this key")
                : Result(index, attachmentId, MemeAnnotationImportOutcome.Imported, overwritten: write == MemeAnnotationWrite.Overwritten);
        }
        catch (DbUpdateException ex)
        {
            // The failed entities stay tracked and would fail every later save of the batch.
            db.ChangeTracker.Clear();
            seenKeys.Remove(key);

            var detail = ex.GetBaseException().Message;
            if (detail.Length > SaveErrorMaxLength)
                detail = detail[..SaveErrorMaxLength];

            // No exception object: the DbContext interceptors already log the failed command.
            logger.LogWarning("Meme annotation import item {Index} (attachment {AttachmentId}, {Model} {PromptVersion}) was refused by the database: {Error}",
                index, attachmentId, item.ModelId, item.PromptVersion, detail);
            return Rejected(index, attachmentId, $"save failed: {detail}");
        }
    }

    // The same two checks as API output: the closed sets at deserialization, then the nulls.
    // Returns the rejection reason, or null when the metadata is valid.
    private static string? ParseMetadata(JsonElement json, out MemeMetadata? metadata)
    {
        try
        {
            metadata = json.Deserialize<MemeMetadata>();
        }
        catch (JsonException ex)
        {
            metadata = null;
            return $"schema violation: {ex.Message}";
        }

        return metadata is null || MemeAttachmentIndexer.HasNullRequiredField(metadata)
            ? "schema violation: null for a required metadata field"
            : null;
    }

    private static string? ValidateKey(MemeAnnotationImportItem item)
    {
        if (!OpenRouterClient.KnownPromptVersions.Contains(item.PromptVersion, StringComparer.Ordinal))
            return $"unknown prompt_version '{item.PromptVersion}' (known: {string.Join(", ", OpenRouterClient.KnownPromptVersions)})";

        // Part of the key: "x " next to "x" would be a second annotation of the same writer.
        if (string.IsNullOrWhiteSpace(item.ModelId) || item.ModelId != item.ModelId.Trim())
            return "model_id is empty or has leading or trailing whitespace";

        return null;
    }

    // One save per item: the annotation and the status flip land together, and a refused
    // item does not take the rest of the batch with it.
    private async Task<MemeAnnotationWrite> WriteAsync(
        MemeAnnotationImportItem item,
        MemeMetadata metadata,
        MemeSampleItem sample,
        DateTime importedAtUtc,
        CancellationToken cancellationToken)
    {
        var row = await indexer.GetOrCreateRowAsync(db, sample, cancellationToken);
        var write = await indexer.ImportAnnotationAsync(db, row, item.ModelId, item.PromptVersion, item.ReasoningEffort,
            metadata, item.IndexedAtUtc?.ToUtcInstant() ?? importedAtUtc, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        // Every item reads its own rows: without this each later save scans all earlier ones.
        db.ChangeTracker.Clear();
        return write;
    }

    private static MemeAnnotationImportItemResult Rejected(int index, ulong? attachmentId, string reason) =>
        Result(index, attachmentId, MemeAnnotationImportOutcome.Rejected, reason);

    private static MemeAnnotationImportItemResult Result(
        int index, ulong? attachmentId, string outcome, string? reason = null, bool overwritten = false) =>
        new MemeAnnotationImportItemResult
        {
            Index = index,
            AttachmentDiscordId = attachmentId?.ToString(),
            Outcome = outcome,
            Overwritten = overwritten,
            Reason = reason,
        };
}
