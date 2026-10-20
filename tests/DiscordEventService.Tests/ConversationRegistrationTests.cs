using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using DSharpPlus;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The Anthropic SDK reads ANTHROPIC_* variables of the whole process. A test that sets one runs
// alone, so no other client in the process sends a request while it is set.
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

// Regression guard for the §6 boot deadlock. Registering DiscordClient in the DSharpPlus child
// container forwards into the very container the client owns, so resolving it re-enters the
// client's own construction during builder.Build() and hangs boot before any log line — which is
// why it slipped past CI (VALIDATE_AND_EXIT could race past it) and only surfaced as an unhealthy,
// silent prod container. The action services must reach the client via DiscordClientAccessor (a
// plain holder set after the client is built), never by registering DiscordClient in a container.
[Collection("ProcessEnvironment")]
public sealed class ConversationRegistrationTests
{
    [Fact]
    public void AddConversationChildServices_NeverRegistersDiscordClientInTheChildContainer()
    {
        var services = new ServiceCollection();
        // The forwards are registered as lambdas and not invoked here, so an empty root provider
        // and empty configuration are enough to inspect what gets registered.
        var emptyRoot = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().Build();

        ConversationRegistration.AddConversationChildServices(services, emptyRoot, configuration);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(DiscordClient));
        // The accessor is the sanctioned, cycle-free way the action services reach the client.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(DiscordClientAccessor));
    }

    // ANTHROPIC_API_KEY stands in for an empty Anthropic:ApiKey in both containers: the child
    // one's IsConfigured gate must agree with the key the root built the chat client with.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnthropicOptions_FallBackToTheConventionalVariable(bool childContainer)
    {
        Assert.Equal("env-key", ApiKey(childContainer, new() { ["ANTHROPIC_API_KEY"] = "env-key" }));
        Assert.Equal("section-key", ApiKey(childContainer, new()
        {
            ["Anthropic:ApiKey"] = "section-key",
            ["ANTHROPIC_API_KEY"] = "env-key",
        }));
        Assert.Null(ApiKey(childContainer, []));
    }

    private static string? ApiKey(bool childContainer, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        if (childContainer)
            ConversationRegistration.AddConversationChildServices(services, new ServiceCollection().BuildServiceProvider(), configuration);
        else
            services.AddConversationFeature(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey;
    }

    // #457: a Claude Code machine often has ANTHROPIC_AUTH_TOKEN set. The SDK would send it as a
    // second credential (Authorization: Bearer) next to the API key on every request.
    [Fact]
    public async Task ApiClient_IgnoresAuthTokenFromEnvironment()
    {
        var handler = await SendOneRequestWith("ANTHROPIC_AUTH_TOKEN", "stray-token");

        var request = Assert.Single(handler.Sent);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(["test-key"], request.Headers.GetValues("X-Api-Key"));
    }

    // One stray line in the server .env must not send every request, key included, to another host.
    [Fact]
    public async Task ApiClient_IgnoresBaseUrlFromEnvironment()
    {
        var handler = await SendOneRequestWith("ANTHROPIC_BASE_URL", "https://bogus.invalid");

        Assert.Equal("api.anthropic.com", Assert.Single(handler.Sent).RequestUri!.Host);
    }

    // The SDK reads ANTHROPIC_CUSTOM_HEADERS once, into headers of every request; no client
    // option turns it off, so the boot names it.
    [Fact]
    public void ApiClient_WarnsWhenCustomHeadersAreSet()
    {
        var log = new RecordingLogger();
        using (new EnvironmentVariable("ANTHROPIC_CUSTOM_HEADERS", "X-Stray: 1"))
            ConversationRegistration.CreateApiClient("test-key", log.For<ConversationRegistrationTests>());

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("ANTHROPIC_CUSTOM_HEADERS"));
    }

    private static async Task<ScriptedClaudeHandler> SendOneRequestWith(string variable, string value)
    {
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("ok"));
        using (new EnvironmentVariable(variable, value))
        {
            var client = handler.CreateChatClient(new ConversationOptions());
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hej")]))
            {
            }
        }
        return handler;
    }

    // Sets a process variable and puts the previous value back on dispose.
    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariable(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
