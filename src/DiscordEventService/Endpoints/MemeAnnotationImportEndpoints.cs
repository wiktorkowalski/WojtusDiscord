using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Endpoints;

internal static class MemeAnnotationImportEndpoints
{
    public const string SecretHeaderName = "X-Import-Secret";

    // One request = one batch. An export bigger than this is split by the caller.
    public const int MaxBatchSize = 500;

    public static void MapMemeAnnotationImportEndpoints(this WebApplication app)
    {
        // On the group: an endpoint added here later is gated too. Replace with the OAuth gate
        // when #339 lands.
        var group = app.MapGroup("/api/ops/meme-annotations")
            .AddEndpointFilter<MemeImportSecretFilter>();

        // Filters run after parameter binding, so the handler takes HttpRequest and reads the body
        // itself: with a body parameter, a request without the secret would be parsed before the 401.
        group.MapPost("/import", ImportAsync)
            .WithName("ImportMemeAnnotations")
            .Produces<MemeAnnotationImportResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);
    }

    // Imports annotations produced outside the bot (#369), for example by an Opus run in Claude Code.
    //
    // Body: a JSON array, at most 500 items:
    //   [{ "attachment_discord_id": "1507736402232741898",   (string or number)
    //      "model_id": "anthropic/claude-opus-5.5",
    //      "prompt_version": "v4",                            (one of OpenRouterClient.KnownPromptVersions)
    //      "indexed_at_utc": "2026-10-03T12:00:00Z",          (optional; default = now)
    //      "reasoning_effort": "low",                         (optional)
    //      "metadata": { ...the MemeMetadata contract, as a model returns it... } }]
    //
    // Run a batch file:
    //   curl -sS -X POST https://<host>/api/ops/meme-annotations/import \
    //     -H "X-Import-Secret: $MEME_IMPORT_SECRET" -H "Content-Type: application/json" \
    //     --data-binary @batch-001.json > result-001.json
    //   jq '{imported, overwritten, skipped, rejected}' result-001.json
    //   jq '.items[] | select(.outcome == "rejected")' result-001.json
    // Split a bigger export first: jq -c '_nwise(500)' all.json | split -l 1 - batch-
    //
    // A re-run is safe: the key is (attachment, model_id, prompt_version). The same metadata is
    // reported as "skipped"; different metadata replaces the stored annotation, also one that the
    // API path wrote under that key. 409 = an indexing job is running; send the batch again later.
    //
    // Needs two settings: MemeIndex__ImportSecret and MemeIndex__ChannelIds__N (an attachment
    // outside those channels is rejected). Neither starts a model call: the live hook and the
    // Sunday sweep stay off until MemeIndex__AutomaticIndexing=true.
    private static async Task<IResult> ImportAsync(
        HttpRequest request,
        IOptions<MemeIndexOptions> memeIndexOptions,
        MemeAnnotationImportService importService,
        DiscordDbContext db,
        CancellationToken cancellationToken)
    {
        if (!memeIndexOptions.Value.IsConfigured)
            return Results.BadRequest(new { error = "MemeIndex:ChannelIds is empty — no meme channels configured" });

        if (!request.HasJsonContentType())
            return Results.BadRequest(new { error = "Content-Type must be application/json" });

        // A running indexing job holds its rows in memory: when its model call fails after the
        // import marked a row Indexed, the job would write Failed over it.
        var indexingActive = (await MemeIndexJobEnqueuer.GetActiveGuildIdsAsync(db, cancellationToken)).Count > 0;
        if (indexingActive)
            return Results.Conflict(new { error = "Meme indexing is in progress — import again when it has finished" });

        JsonElement body;
        try
        {
            body = await request.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = $"The body is not valid JSON: {ex.Message}" });
        }

        if (body.ValueKind != JsonValueKind.Array)
            return Results.BadRequest(new { error = "The body must be a JSON array of items" });

        var count = body.GetArrayLength();
        if (count == 0)
            return Results.BadRequest(new { error = "The batch is empty" });
        if (count > MaxBatchSize)
            return Results.BadRequest(new { error = $"The batch has {count} items; the limit is {MaxBatchSize}. Split it." });

        return Results.Ok(await importService.ImportAsync([.. body.EnumerateArray()], cancellationToken));
    }
}

// Shared-secret gate for the import (#369). Ops endpoints have no authentication until #339.
internal sealed class MemeImportSecretFilter(
    IOptions<MemeIndexOptions> memeIndexOptions,
    ILogger<MemeImportSecretFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var secret = memeIndexOptions.Value.ImportSecret?.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            // An unset secret must not mean an open endpoint.
            logger.LogWarning("Meme annotation import refused: MemeIndex:ImportSecret is not configured");
            return Results.Unauthorized();
        }

        var provided = context.HttpContext.Request.Headers[MemeAnnotationImportEndpoints.SecretHeaderName].ToString();
        if (!SecretsMatch(provided, secret))
        {
            logger.LogWarning("Meme annotation import refused: the {Header} header is missing or wrong",
                MemeAnnotationImportEndpoints.SecretHeaderName);
            return Results.Unauthorized();
        }

        return await next(context);
    }

    // Hashes first: FixedTimeEquals returns early on a length difference, which would leak the length.
    private static bool SecretsMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
