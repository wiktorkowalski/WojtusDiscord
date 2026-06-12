using System.Net;
using System.Text;
using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class OpenRouterClientTests
{
    private static readonly byte[] FakeImage = [0xFF, 0xD8, 0xFF, 0x00];

    [Fact]
    public async Task AnalyzeImageAsync_OnSuccess_ReturnsMetadataAndUsage()
    {
        var content = JsonSerializer.Serialize(new
        {
            description_pl = "Opis po polsku",
            description_en = "English description",
            ocr_text = "some text",
            tags = new[] { "kot", "cat" },
            source = "reddit",
            template = (string?)null
        });
        // Capture inside the handler — the client disposes the request after sending.
        string? sentBody = null;
        string? sentAuthScheme = null;
        var client = NewClient(req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            sentAuthScheme = req.Headers.Authorization?.Scheme;
            return Json(HttpStatusCode.OK, CompletionBody(content, finishReason: "stop", cost: 0.0123m));
        });

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "google/gemini-2.5-flash", CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Success, result.Outcome);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Opis po polsku", result.Metadata!.DescriptionPl);
        Assert.Equal(["kot", "cat"], result.Metadata.Tags);
        Assert.Equal("reddit", result.Metadata.Source);
        Assert.Null(result.Metadata.Template);
        Assert.Equal(10, result.Usage!.PromptTokens);
        Assert.Equal(20, result.Usage.CompletionTokens);
        Assert.Equal(0.0123m, result.Usage.CostUsd);

        Assert.Equal("Bearer", sentAuthScheme);
        Assert.NotNull(sentBody);
        Assert.Contains("\"json_schema\"", sentBody);
        Assert.Contains("google/gemini-2.5-flash", sentBody);
        Assert.Contains("data:image/jpeg;base64,", sentBody);
    }

    [Theory]
    [InlineData("stop", "no thanks")]
    [InlineData("content_filter", null)]
    public async Task AnalyzeImageAsync_OnRefusal_ReturnsRefusalOutcome(string finishReason, string? refusal)
    {
        var client = NewClient(_ => Json(HttpStatusCode.OK,
            CompletionBody(content: null, finishReason: finishReason, cost: null, refusal: refusal)));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Refusal, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task AnalyzeImageAsync_OnHttpError_MapsTransience(HttpStatusCode status, bool expectTransient)
    {
        var client = NewClient(_ => Json(status, "{\"error\":\"boom\"}"));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.Equal(expectTransient, result.IsTransient);
    }

    [Fact]
    public async Task AnalyzeImageAsync_OnLengthTruncation_NamesTheCause()
    {
        var client = NewClient(_ => Json(HttpStatusCode.OK,
            CompletionBody("{\"description_pl\":\"truncat", finishReason: "length", cost: null)));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.Contains("truncated", result.Error);
    }

    [Fact]
    public async Task AnalyzeImageAsync_OnSchemaViolatingContent_ReturnsNonTransientError()
    {
        var client = NewClient(_ => Json(HttpStatusCode.OK,
            CompletionBody("this is not the agreed json", finishReason: "stop", cost: null)));

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithoutApiKey_FailsWithoutCalling()
    {
        var called = false;
        var client = NewClient(_ => { called = true; return Json(HttpStatusCode.OK, "{}"); }, apiKey: "");

        var result = await client.AnalyzeImageAsync(FakeImage, "image/jpeg", "m", CancellationToken.None);

        Assert.Equal(MemeAnalysisOutcome.Error, result.Outcome);
        Assert.False(result.IsTransient);
        Assert.False(called);
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

    private static OpenRouterClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond, string apiKey = "test-key")
    {
        var options = Options.Create(new OpenRouterOptions { ApiKey = apiKey });
        return new OpenRouterClient(new StubHttpClientFactory(new StubHandler(respond)), options, NullLogger<OpenRouterClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://openrouter.test/api/v1/") };
    }
}
