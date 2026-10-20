using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The startup validation of Conversation:* (#457). It goes through the real registration and
// IOptions resolution, not Validator.TryValidateObject: that one neither descends into
// [ValidateObjectMembers] nor runs the .Validate(...) predicates, so it would pass with no checks.
public sealed class ConversationOptionsTests
{
    [Fact]
    public void Defaults_PassValidation() => Assert.Empty(Failures([]));

    [Theory]
    [InlineData("WebSearch:MaxUses", "0")]
    [InlineData("WebSearch:MaxUses", "-1")]
    [InlineData("Model", "")]
    [InlineData("Model", " ")]
    [InlineData("RetryMaxAttempts", "0")]
    [InlineData("RetryBaseDelayMs", "-1")]
    [InlineData("RetryMaxDelayMs", "-1")]
    // int.MaxValue overflowed random.Next(0, ceiling + 1) in ConversationRetryPolicy.
    [InlineData("RetryMaxDelayMs", "2147483647")]
    [InlineData("RetryBaseDelayMs", "2147483647")]
    // An effort the API does not know is a 400 on every call.
    [InlineData("ReasoningEffort", "extreme")]
    [InlineData("ReasoningEffort", "")]
    [InlineData("MaxOutputTokens", "1023")]
    [InlineData("MaxOutputTokens", "128001")]
    public void InvalidValue_FailsValidation(string key, string value) =>
        Assert.NotEmpty(Failures(new() { ["Conversation:" + key] = value }));

    // The API answers 400 when a web search tool carries both lists.
    [Fact]
    public void WebSearch_BothDomainLists_FailsValidation()
    {
        Assert.NotEmpty(Failures(new()
        {
            ["Conversation:WebSearch:AllowedDomains:0"] = "example.com",
            ["Conversation:WebSearch:BlockedDomains:0"] = "example.org",
        }));
        Assert.Empty(Failures(new() { ["Conversation:WebSearch:BlockedDomains:0"] = "example.org" }));
    }

    [Fact]
    public void EveryKnownReasoningEffort_PassesValidationAndParses()
    {
        foreach (var effort in ConversationOptions.KnownReasoningEfforts.Split('|'))
        {
            Assert.Empty(Failures(new() { ["Conversation:ReasoningEffort"] = effort }));
            AnthropicChatOptions.ParseEffort(effort);
        }
    }

    // The failures startup (ValidateOnStart) would report for this configuration.
    private static IEnumerable<string> Failures(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddConversationFeature(configuration).BuildServiceProvider();
        try
        {
            _ = provider.GetRequiredService<IOptions<ConversationOptions>>().Value;
            return [];
        }
        catch (OptionsValidationException ex)
        {
            return ex.Failures;
        }
    }
}
