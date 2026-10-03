using System.ComponentModel.DataAnnotations;
using System.Text;
using DiscordEventService.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class OpenRouterOptionsTests
{
    [Fact]
    public void BenchmarkModels_NothingConfigured_IsEmpty()
    {
        var options = Bind(new ConfigurationBuilder());

        Assert.Empty(options.BenchmarkModels);
    }

    // The binder appends BenchmarkModels__N to a code default. An empty default is what makes
    // a run use exactly the configured slots (#366).
    [Fact]
    public void BenchmarkModels_ConfiguredByIndex_AreExactlyTheConfiguredSlots()
    {
        var options = Bind(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenRouter:BenchmarkModels:0"] = "google/gemini-3.8-flash|effort=low",
            ["OpenRouter:BenchmarkModels:1"] = "google/gemini-3-flash-preview",
        }));

        Assert.Equal(["google/gemini-3.8-flash|effort=low", "google/gemini-3-flash-preview"], options.BenchmarkModels);
    }

    // appsettings.json ships "ReasoningEffort": null — that has to mean unset, not a startup failure.
    [Fact]
    public void ReasoningEffort_JsonNull_IsUnsetAndValid()
    {
        var json = new MemoryStream(Encoding.UTF8.GetBytes("""{ "OpenRouter": { "ReasoningEffort": null } }"""));

        var options = Bind(new ConfigurationBuilder().AddJsonStream(json));

        Assert.True(string.IsNullOrEmpty(options.ReasoningEffort));
        Assert.True(IsValid(options));
    }

    [Fact]
    public void ReasoningEffort_ConfiguredOverJsonNull_Wins()
    {
        var json = new MemoryStream(Encoding.UTF8.GetBytes("""{ "OpenRouter": { "ReasoningEffort": null } }"""));

        var options = Bind(new ConfigurationBuilder()
            .AddJsonStream(json)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenRouter:ReasoningEffort"] = "low" }));

        Assert.Equal("low", options.ReasoningEffort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("minimal")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void ReasoningEffort_UnsetOrKnownValue_PassesValidation(string? effort)
    {
        Assert.True(IsValid(new OpenRouterOptions { ReasoningEffort = effort }));
    }

    [Theory]
    [InlineData("lwo")]
    [InlineData("Low")]
    [InlineData(" low")]
    [InlineData("low\n")]
    [InlineData(" ")]
    public void ReasoningEffort_UnknownValue_FailsValidation(string effort)
    {
        Assert.False(IsValid(new OpenRouterOptions { ReasoningEffort = effort }));
    }

    // The same check ValidateDataAnnotations runs at startup.
    private static bool IsValid(OpenRouterOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), validationResults: null, validateAllProperties: true);

    private static OpenRouterOptions Bind(IConfigurationBuilder builder)
    {
        var options = new OpenRouterOptions();
        builder.Build().GetSection(OpenRouterOptions.SectionName).Bind(options);
        return options;
    }
}
