using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordEventService.Configuration;
using DiscordEventService.Data.Entities.Core;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

internal sealed class OpenRouterClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterClient> logger)
{
    public const string HttpClientName = "openrouter";

    // Stored on every annotation as half of its key. Bump it by hand with ANY change to
    // SystemPrompt or ResponseSchema — an unchanged version makes new output look like old output.
    public const string PromptVersion = "v4";

    // The prompt versions whose output has the MemeMetadata shape. The import (#369) takes only
    // these: an annotation under another version would not be what its key says it is.
    public static readonly string[] KnownPromptVersions = [PromptVersion];

    // v4 (#368) = v2 plus the schema v2 fields. v2's wording is benchmark-measured (#223): template
    // fill 40→52; v3's stricter template definition scored lower, so it is not reused here.
    private const string SystemPrompt =
        """
        You analyze meme images from a Polish Discord community and produce search metadata.
        People will later find these memes by typing a few words in Polish or English, so choose the words they would actually type.
        Rules:
        - description_pl: 1-3 zdania po polsku — co przedstawia mem i o czym jest.
        - description_en: 1-3 sentences in English describing what the meme shows and what it is about.
        - ocr_text: ALL text visible in the image, verbatim, in its original language, preserving line breaks. Empty string if there is no text.
        - tags: 10-20 lowercase keywords mixing BOTH Polish and English: topics, objects, people, characters, shows, games, emotions/tone, recognizable technologies/brands. Duplicate the same concept in both languages (e.g. both "kot" and "cat").
        - image_kind: exactly one of: template_meme (a known meme template or recurring format), screenshot_post_or_chat (a screenshot of a post, comment or chat), comic (a drawn comic or multi-panel cartoon), photo_with_caption (a photo with caption text added), cutout_face_or_emote (a cut-out face, sticker or emote with little or no context, usually of a private person), edited_photo (a photoshopped or otherwise edited photo), video_frame (a frame from a film, show, stream or video), other.
        - templates: the canonical, most commonly used names of the meme templates or recurring meme formats in the image, as people would search for them. Usually one; several when the image fits more than one. Covers international templates (e.g. "drake", "distracted boyfriend", "doge", "this is fine", "gigachad", "wojak", "stonks") AND Polish ones (e.g. "paski tvp", "cenzopapa", "nosacz sundajski", "świat według kiepskich", "kononowicz", "typowy polak"). A description of the scene is not a template. Plain screenshots of posts or chats are not templates. Empty list when no recognizable template or recurring format is present.
        - people: real people shown or named in the image. Add a person ONLY when their name is visible in the image (evidence "name_visible") or they are a widely recognized public figure (evidence "widely_recognized"). Never guess the identity of a private person from appearance alone — describe the person instead. For image_kind cutout_face_or_emote this list MUST be empty and no other field may name the person.
        - search_phrases: 3-6 short phrases in Polish or English that a person would actually type to find this exact meme.
        - franchise: the game, show, film or other franchise the image comes from or refers to, or null if none.
        - source: the platform whose watermark or UI is visible in the image. Use "other" for a visible platform that is not on the list, and "none" when no platform is visible.
        - language: the language of the text in the image: pl, en, mixed, or none when there is no text.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object StringArraySchema = new { type = "array", items = new { type = "string" } };

    // strict json_schema makes the model return the MemeMetadata contract directly (no
    // markdown-fence scraping): every property required, nullability expressed in types.
    // Kept to what every provider's strict mode takes: no minItems/maxItems, and no null inside
    // an enum — "no source" is the string "none" (#223 research).
    private static readonly object ResponseSchema = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "meme_metadata",
            strict = true,
            schema = new
            {
                type = "object",
                additionalProperties = false,
                required = new[]
                {
                    "description_pl", "description_en", "ocr_text", "tags", "image_kind", "templates",
                    "people", "search_phrases", "franchise", "source", "language",
                },
                properties = new
                {
                    description_pl = new { type = "string" },
                    description_en = new { type = "string" },
                    ocr_text = new { type = "string" },
                    tags = StringArraySchema,
                    image_kind = new { type = "string", @enum = MemeJsonNames.Of<MemeImageKind>() },
                    templates = StringArraySchema,
                    people = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "name", "evidence" },
                            properties = new
                            {
                                name = new { type = "string" },
                                evidence = new { type = "string", @enum = MemeJsonNames.Of<MemePersonEvidence>() },
                            },
                        },
                    },
                    search_phrases = StringArraySchema,
                    franchise = new { type = new[] { "string", "null" } },
                    source = new { type = "string", @enum = (string[])[.. MemeSources.Known, MemeSources.None] },
                    language = new { type = "string", @enum = MemeJsonNames.Of<MemeLanguage>() },
                },
            },
        },
    };

    public async Task<MemeAnalysisResult> AnalyzeImageAsync(
        byte[] imageBytes,
        string mimeType,
        string model,
        string? reasoningEffort,
        CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.IsConfigured)
            return MemeAnalysisResult.Failed("OpenRouter:ApiKey is not configured", isTransient: false);

        var payload = BuildAnalysisPayload(imageBytes, mimeType, model, reasoningEffort, opts);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        string body;
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            response = await client.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "OpenRouter request failed (transport) for model {Model}", model);
            return MemeAnalysisResult.Failed($"transport: {ex.Message}", isTransient: true);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 408/429/5xx are worth retrying later; 4xx config/payload errors are not.
                var transient = response.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    or >= HttpStatusCode.InternalServerError;
                logger.LogWarning("OpenRouter returned {StatusCode} for model {Model}: {Body}",
                    (int)response.StatusCode, model, Truncate(body, 500));
                return MemeAnalysisResult.Failed($"HTTP {(int)response.StatusCode}: {Truncate(body, 500)}", transient);
            }
        }

        return ParseResponse(body, model);
    }

    private static object BuildAnalysisPayload(byte[] imageBytes, string mimeType, string model, string? reasoningEffort, OpenRouterOptions opts) => new
    {
        model,
        messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}" },
                    },
                },
            },
        },
        response_format = ResponseSchema,
        max_tokens = opts.MaxOutputTokens,
        temperature = 0.2,
        // Opt-in per caller (#366): null drops the field (JsonOptions skips nulls) and the model
        // runs at its own default. Never force one effort on every model — effort=low made
        // gemini-2.5-flash return empty content.
        reasoning = string.IsNullOrEmpty(reasoningEffort) ? null : new { effort = reasoningEffort },
        // include=true returns the real cost from OpenRouter instead of us keeping price tables.
        usage = new { include = true },
    };

    private MemeAnalysisResult ParseResponse(string body, string model)
    {
        ChatCompletionResponse? completion;
        try
        {
            completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OpenRouter response was not valid JSON for model {Model}", model);
            return MemeAnalysisResult.Failed($"invalid response JSON: {ex.Message}", isTransient: true);
        }

        var choice = completion?.Choices is [{ } first, ..] ? first : null;
        if (choice?.Message is null)
            return MemeAnalysisResult.Failed("response contained no choices", isTransient: true);

        if (!string.IsNullOrEmpty(choice.Message.Refusal) ||
            string.Equals(choice.FinishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
        {
            return MemeAnalysisResult.Refusal(choice.Message.Refusal ?? "content_filter");
        }

        if (string.Equals(choice.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
            return MemeAnalysisResult.Failed(
                "output truncated (finish_reason=length) — raise OpenRouter:MaxOutputTokens", isTransient: false);

        if (string.IsNullOrWhiteSpace(choice.Message.Content))
            return MemeAnalysisResult.Failed($"empty content (finish_reason={choice.FinishReason})", isTransient: true);

        MemeMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<MemeMetadata>(choice.Message.Content);
        }
        catch (JsonException ex)
        {
            // strict schema should make this impossible; treat as a model bug, not retryable.
            // The output goes to Debug only: for a cut-out it can hold the name the rule drops (#368).
            logger.LogWarning(ex, "Model {Model} violated the response schema", model);
            logger.LogDebug("Model {Model} output that violated the response schema: {Content}",
                model, Truncate(choice.Message.Content, 500));
            return MemeAnalysisResult.Failed($"schema violation: {ex.Message}", isTransient: false);
        }

        if (metadata is null)
            return MemeAnalysisResult.Failed("schema violation: null content", isTransient: false);

        var usage = new MemeAnalysisUsage(
            completion!.Usage?.PromptTokens ?? 0,
            completion.Usage?.CompletionTokens ?? 0,
            completion.Usage?.Cost);

        return MemeAnalysisResult.Success(metadata, usage, choice.Message.Content);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] UsageInfo? Usage);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ChoiceMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChoiceMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("refusal")] string? Refusal);

    private sealed record UsageInfo(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("cost")] decimal? Cost);
}
