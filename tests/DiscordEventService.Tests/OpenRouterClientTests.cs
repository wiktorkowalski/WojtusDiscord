using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class OpenRouterClientTests
{
    private static readonly byte[] FakeImage = [0xFF, 0xD8, 0xFF, 0x00];

    // The contract, in the order the strict schema lists it.
    private static readonly string[] MetadataMembers =
    [
        "description_pl", "description_en", "ocr_text", "tags", "image_kind", "templates",
        "people", "search_phrases", "franchise", "source", "language",
    ];

    [Fact]
    public async Task AnalyzeImageAsync_OnSuccess_ReturnsMetadataAndUsage()
    {
        var content = FullMetadata().ToJsonString();
        // Capture inside the handler — the client disposes the request after sending.
        string? sentBody = null;
        string? sentAuthScheme = null;
        var client = NewClient(async req =>
        {
            sentBody = await req.Content!.ReadAsStringAsync();
            sentAuthScheme = req.Headers.Authorization?.Scheme;
            return Json(HttpStatusCode.OK, CompletionBody(content, finishReason: "stop", cost: 0.0123m));
        });

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "google/gemini-2.5-flash", reasoningEffort: null, CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Success, result.Outcome);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Opis po polsku", result.Metadata!.DescriptionPl);
        Assert.Equal(["kot", "cat"], result.Metadata.Tags);
        Assert.Equal("reddit", result.Metadata.Source);
        Assert.Equal(10, result.Usage!.PromptTokens);
        Assert.Equal(20, result.Usage.CompletionTokens);
        Assert.Equal(0.0123m, result.Usage.CostUsd);

        Assert.Equal("Bearer", sentAuthScheme);
        Assert.NotNull(sentBody);
        Assert.Contains("\"json_schema\"", sentBody);
        Assert.Contains("google/gemini-2.5-flash", sentBody);
        Assert.Contains("data:image/jpeg;base64,", sentBody);
    }

    [Fact]
    public async Task AnalyzeImageAsync_FullSchemaV2Output_ParsesEveryMember()
    {
        var result = await AnalyzeAsync(FullMetadata());

        Assert.Equal(MemeAnalysisOutcome.Success, result.Outcome);
        var metadata = result.Metadata!;
        Assert.Equal("Opis po polsku", metadata.DescriptionPl);
        Assert.Equal("English description", metadata.DescriptionEn);
        Assert.Equal("some text", metadata.OcrText);
        Assert.Equal(["kot", "cat"], metadata.Tags);
        Assert.Equal(MemeImageKind.PhotoWithCaption, metadata.ImageKind);
        Assert.Equal(["drake", "paski tvp"], metadata.Templates);
        Assert.Equal(
            [("Adam Małysz", MemePersonEvidence.WidelyRecognized), ("Jan Nowak", MemePersonEvidence.NameVisible)],
            metadata.People.Select(p => (p.Name, p.Evidence)));
        Assert.Equal(["kot w lodówce", "cat in the fridge"], metadata.SearchPhrases);
        Assert.Equal("Wiedźmin", metadata.Franchise);
        Assert.Equal("reddit", metadata.Source);
        Assert.Equal(MemeLanguage.Mixed, metadata.Language);
    }

    // The writer stores the raw content as provenance, so it must be the model's own text.
    [Fact]
    public async Task AnalyzeImageAsync_OnSuccess_KeepsTheVerbatimContent()
    {
        var content = FullMetadata().ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var result = await AnalyzeAsync(content);

        Assert.Equal(content, result.RawContent);
    }

    // The closed sets fail in the parser, before anything is stored: a name outside the set, the
    // wrong case, and a number instead of a name.
    [Theory]
    [InlineData("image_kind", "\"meme\"")]
    [InlineData("image_kind", "3")]
    [InlineData("language", "\"de\"")]
    [InlineData("language", "1")]
    [InlineData("source", "\"Twitter\"")]
    [InlineData("source", "\"x\"")]
    [InlineData("source", "7")]
    [InlineData("people", """[{"name":"Jan Nowak","evidence":"looks_like_him"}]""")]
    [InlineData("people", """[{"name":"Jan Nowak","evidence":1}]""")]
    public async Task AnalyzeImageAsync_ValueOutsideAClosedSet_IsANonTransientSchemaViolation(string member, string valueJson)
    {
        var metadata = FullMetadata();
        metadata[member] = JsonNode.Parse(valueJson);

        var result = await AnalyzeAsync(metadata);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.StartsWith("schema violation", result.Error);
        Assert.Null(result.Metadata);
    }

    // System.Text.Json reads "a, b" as a flags list and ORs the values, also for an enum that is
    // not [Flags]. "cutout_face_or_emote, comic" would become video_frame and skip the cut-out rule.
    [Theory]
    [InlineData("image_kind", "\"cutout_face_or_emote, comic\"")]
    [InlineData("language", "\"mixed, en\"")]
    [InlineData("people", """[{"name":"Jan Nowak","evidence":"name_visible, widely_recognized"}]""")]
    public async Task AnalyzeImageAsync_CommaSeparatedNamesInAClosedSet_IsANonTransientSchemaViolation(string member, string valueJson)
    {
        var metadata = FullMetadata();
        metadata[member] = JsonNode.Parse(valueJson);

        var result = await AnalyzeAsync(metadata);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.StartsWith("schema violation", result.Error);
    }

    [Theory]
    [InlineData("description_pl")]
    [InlineData("description_en")]
    [InlineData("ocr_text")]
    [InlineData("tags")]
    [InlineData("image_kind")]
    [InlineData("templates")]
    [InlineData("people")]
    [InlineData("search_phrases")]
    [InlineData("franchise")]
    [InlineData("source")]
    [InlineData("language")]
    public async Task AnalyzeImageAsync_OutputWithoutARequiredMember_IsANonTransientSchemaViolation(string member)
    {
        var metadata = FullMetadata();
        Assert.True(metadata.Remove(member));

        var result = await AnalyzeAsync(metadata);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.StartsWith("schema violation", result.Error);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("evidence")]
    public async Task AnalyzeImageAsync_PersonWithoutARequiredMember_IsANonTransientSchemaViolation(string member)
    {
        var metadata = FullMetadata();
        Assert.True(metadata["people"]![0]!.AsObject().Remove(member));

        var result = await AnalyzeAsync(metadata);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.StartsWith("schema violation", result.Error);
    }

    // "none" is the contract's word for no platform; storage says NULL. An explicit null means the same.
    [Theory]
    [InlineData("\"none\"")]
    [InlineData("null")]
    public async Task AnalyzeImageAsync_SourceNoneOrNull_ParsesAsNullSource(string sourceJson)
    {
        var metadata = FullMetadata();
        metadata["source"] = JsonNode.Parse(sourceJson);

        var result = await AnalyzeAsync(metadata);

        Assert.Equal(MemeAnalysisOutcome.Success, result.Outcome);
        Assert.Null(result.Metadata!.Source);
    }

    [Fact]
    public async Task AnalyzeImageAsync_NullFranchise_ParsesAsNullFranchise()
    {
        var metadata = FullMetadata();
        metadata["franchise"] = null;

        var result = await AnalyzeAsync(metadata);

        Assert.Equal(MemeAnalysisOutcome.Success, result.Outcome);
        Assert.Null(result.Metadata!.Franchise);
    }

    [Fact]
    public async Task AnalyzeImageAsync_ResponseSchema_IsStrictAndRequiresAllElevenMembers()
    {
        var format = (await CaptureRequestBodyAsync(reasoningEffort: null)).GetProperty("response_format");

        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("json_schema").GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(MetadataMembers, Strings(schema.GetProperty("required")));
        Assert.Equal(MetadataMembers, schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    // The model sees these lists; the parser accepts exactly these names.
    [Fact]
    public async Task AnalyzeImageAsync_ResponseSchema_ListsEveryClosedSetAsAnEnum()
    {
        var properties = await CaptureSchemaPropertiesAsync();

        Assert.Equal(
            [
                "template_meme", "screenshot_post_or_chat", "comic", "photo_with_caption",
                "cutout_face_or_emote", "edited_photo", "video_frame", "other",
            ],
            Strings(properties.GetProperty("image_kind").GetProperty("enum")));
        Assert.Equal(["pl", "en", "mixed", "none"], Strings(properties.GetProperty("language").GetProperty("enum")));
        Assert.Equal([.. MemeSources.Known, "none"], Strings(properties.GetProperty("source").GetProperty("enum")));
        Assert.Equal(MemeJsonNames.Of<MemeImageKind>(), Strings(properties.GetProperty("image_kind").GetProperty("enum")));
        Assert.Equal(MemeJsonNames.Of<MemeLanguage>(), Strings(properties.GetProperty("language").GetProperty("enum")));
    }

    [Fact]
    public async Task AnalyzeImageAsync_ResponseSchema_PeopleItemsAreNameAndClosedEvidenceOnly()
    {
        var person = (await CaptureSchemaPropertiesAsync()).GetProperty("people").GetProperty("items");

        Assert.Equal("object", person.GetProperty("type").GetString());
        Assert.False(person.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["name", "evidence"], Strings(person.GetProperty("required")));
        Assert.Equal(["name", "evidence"], person.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        var evidence = Strings(person.GetProperty("properties").GetProperty("evidence").GetProperty("enum"));
        Assert.Equal(["name_visible", "widely_recognized"], evidence);
        Assert.Equal(MemeJsonNames.Of<MemePersonEvidence>(), evidence);
    }

    // franchise is the one member whose type lets the model say null; source says "none" instead,
    // because not every provider's strict mode takes a null inside an enum.
    [Fact]
    public async Task AnalyzeImageAsync_ResponseSchema_OnlyFranchiseIsNullable()
    {
        var properties = await CaptureSchemaPropertiesAsync();

        Assert.Equal(["string", "null"], Strings(properties.GetProperty("franchise").GetProperty("type")));
        Assert.All(
            properties.EnumerateObject().Where(p => p.Name != "franchise"),
            p => Assert.Equal(JsonValueKind.String, p.Value.GetProperty("type").ValueKind));
        Assert.DoesNotContain(Strings(properties.GetProperty("source").GetProperty("enum")), value => value is null);
    }

    // Not every provider's strict mode takes item counts (#223 research).
    [Fact]
    public async Task AnalyzeImageAsync_ResponseSchema_HasNoItemCountLimits()
    {
        var format = (await CaptureRequestBodyAsync(reasoningEffort: null)).GetProperty("response_format").GetRawText();

        Assert.DoesNotContain("minItems", format);
        Assert.DoesNotContain("maxItems", format);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithReasoningEffort_SendsItAsReasoningEffort()
    {
        var sent = await CaptureRequestBodyAsync(reasoningEffort: "low");

        Assert.Equal("low", sent.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal(4000, sent.GetProperty("max_tokens").GetInt32());
    }

    // Unset must stay off the wire: a forced effort made gemini-2.5-flash return empty content.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnalyzeImageAsync_WithoutReasoningEffort_SendsNoReasoningField(string? reasoningEffort)
    {
        var sent = await CaptureRequestBodyAsync(reasoningEffort);

        Assert.False(sent.TryGetProperty("reasoning", out _));
        Assert.Equal(4000, sent.GetProperty("max_tokens").GetInt32());
    }

    // PromptVersion is half of an annotation's key (#367). A prompt or schema edit under the same
    // version makes new output look like old output. This pin fails until both move together:
    // bump OpenRouterClient.PromptVersion, then update the version and the fingerprint here.
    [Fact]
    public async Task AnalyzeImageAsync_PromptAndSchema_MatchThePinnedPromptVersion()
    {
        var sent = await CaptureRequestBodyAsync(reasoningEffort: null);

        var prompt = sent.GetProperty("messages")[0].GetProperty("content").GetString()!.ReplaceLineEndings("\n");
        var schema = sent.GetProperty("response_format").GetRawText();
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt + "\n" + schema)));

        Assert.Equal("v4", OpenRouterClient.PromptVersion);
        Assert.Equal("a2df0f665691fe8908f4c3fe8315950352914f8f7d2b0c48bd7bc8f350bb0035", fingerprint);
    }

    [Theory]
    [InlineData("stop", "no thanks")]
    [InlineData("content_filter", null)]
    public async Task AnalyzeImageAsync_OnRefusal_ReturnsRefusalOutcome(string finishReason, string? refusal)
    {
        var client = NewClient(_ => JsonTask(HttpStatusCode.OK,
            CompletionBody(content: null, finishReason: finishReason, cost: null, refusal: refusal)));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort: null, CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Refusal, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task AnalyzeImageAsync_OnHttpError_MapsTransience(HttpStatusCode status, bool expectTransient)
    {
        var client = NewClient(_ => JsonTask(status, "{\"error\":\"boom\"}"));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort: null, CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.Equal(expectTransient, result.IsTransient);
    }

    [Fact]
    public async Task AnalyzeImageAsync_OnLengthTruncation_NamesTheCause()
    {
        var client = NewClient(_ => JsonTask(HttpStatusCode.OK,
            CompletionBody("{\"description_pl\":\"truncat", finishReason: "length", cost: null)));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort: null, CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.Contains("truncated", result.Error);
    }

    [Fact]
    public async Task AnalyzeImageAsync_OnSchemaViolatingContent_ReturnsNonTransientError()
    {
        var client = NewClient(_ => JsonTask(HttpStatusCode.OK,
            CompletionBody("this is not the agreed json", finishReason: "stop", cost: null)));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort: null, CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithoutApiKey_FailsWithoutCalling()
    {
        var called = false;
        var client = NewClient(_ => { called = true; return JsonTask(HttpStatusCode.OK, "{}"); }, apiKey: "");

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort: null, CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.False(called);
    }

    // A complete schema v2 output; a test changes the one member it is about.
    private static JsonObject FullMetadata() => new JsonObject
    {
        ["description_pl"] = "Opis po polsku",
        ["description_en"] = "English description",
        ["ocr_text"] = "some text",
        ["tags"] = new JsonArray("kot", "cat"),
        ["image_kind"] = "photo_with_caption",
        ["templates"] = new JsonArray("drake", "paski tvp"),
        ["people"] = new JsonArray(
            new JsonObject { ["name"] = "Adam Małysz", ["evidence"] = "widely_recognized" },
            new JsonObject { ["name"] = "Jan Nowak", ["evidence"] = "name_visible" }),
        ["search_phrases"] = new JsonArray("kot w lodówce", "cat in the fridge"),
        ["franchise"] = "Wiedźmin",
        ["source"] = "reddit",
        ["language"] = "mixed",
    };

    private static Task<MemeAnalysisResult> AnalyzeAsync(JsonObject metadata) => AnalyzeAsync(metadata.ToJsonString());

    private static Task<MemeAnalysisResult> AnalyzeAsync(string content)
    {
        var client = NewClient(_ => JsonTask(HttpStatusCode.OK, CompletionBody(content, finishReason: "stop", cost: null)));
        return client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort: null, CancellationToken.None);
    }

    private static async Task<JsonElement> CaptureSchemaPropertiesAsync() =>
        (await CaptureRequestBodyAsync(reasoningEffort: null))
            .GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("properties");

    private static IEnumerable<string?> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString());

    private static async Task<JsonElement> CaptureRequestBodyAsync(string? reasoningEffort)
    {
        // Capture inside the handler — the client disposes the request after sending.
        string? sentBody = null;
        var client = NewClient(async req =>
        {
            sentBody = await req.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, CompletionBody(content: null, finishReason: "stop", cost: null));
        });

        await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", reasoningEffort, CancellationToken.None);

        using var document = JsonDocument.Parse(sentBody!);
        return document.RootElement.Clone();
    }

    private static string CompletionBody(string? content, string finishReason, decimal? cost, string? refusal = null) =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    finish_reason = finishReason,
                    message = new { content, refusal }
                }
            },
            usage = new { prompt_tokens = 10, completion_tokens = 20, cost }
        });

    private static OpenRouterClient NewClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, string apiKey = "test-key")
    {
        var options = Options.Create(new OpenRouterOptions { ApiKey = apiKey });
        return new OpenRouterClient(new StubHttpClientFactory(new StubHandler(respond)), options, NullLogger<OpenRouterClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Task<HttpResponseMessage> JsonTask(HttpStatusCode status, string body) =>
        Task.FromResult(Json(status, body));

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://openrouter.test/api/v1/") };
    }
}
