using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordEventService.Configuration;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.MemeIndexing;

internal sealed class OpenRouterClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterClient> logger)
{
    public const string HttpClientName = "openrouter";

    private const string SystemPrompt =
        """
        You analyze meme images from a Polish Discord community and produce search metadata.
        Rules:
        - description_pl: 1-3 zdania po polsku — co przedstawia mem i o czym jest.
        - description_en: 1-3 sentences in English describing what the meme shows and what it is about.
        - ocr_text: ALL text visible in the image, verbatim, in its original language, preserving line breaks. Empty string if there is no text.
        - tags: 10-20 lowercase keywords mixing BOTH Polish and English: topics, objects, people, emotions/tone, recognizable technologies/brands, meme template name. Duplicate the same concept in both languages (e.g. both "kot" and "cat").
        - source: the platform whose watermark or UI is visible in the image (e.g. reddit, twitter, x, facebook, instagram, tiktok, kwejk, jbzd, 9gag, demotywatory, wykop), or null if none is visible.
        - template: the canonical meme template name (e.g. "drake", "distracted boyfriend", "doge"), or null if not a recognizable template.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // strict json_schema makes the model return the MemeMetadata contract directly (no
    // markdown-fence scraping): every property required, nullability expressed in types.
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
                required = new[] { "description_pl", "description_en", "ocr_text", "tags", "source", "template" },
                properties = new
                {
                    description_pl = new { type = "string" },
                    description_en = new { type = "string" },
                    ocr_text = new { type = "string" },
                    tags = new { type = "array", items = new { type = "string" } },
                    source = new { type = new[] { "string", "null" } },
                    template = new { type = new[] { "string", "null" } },
                },
            },
        },
    };

    public async Task<MemeAnalysisResult> AnalyzeImageAsync(
        byte[] imageBytes,
        string mimeType,
        string model,
        CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.IsConfigured)
            return MemeAnalysisResult.Failed("OpenRouter:ApiKey is not configured", isTransient: false);

        var payload = new
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
            // No reasoning override: effort=low made gemini-2.5-flash return
            // empty content; the raised max_tokens budget is what thinking
            // models actually need.
            response_format = ResponseSchema,
            max_tokens = opts.MaxOutputTokens,
            temperature = 0.2,
            // include=true returns the real cost from OpenRouter instead of us keeping price tables.
            usage = new { include = true },
        };

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
            logger.LogWarning(ex, "Model {Model} violated the response schema: {Content}",
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
