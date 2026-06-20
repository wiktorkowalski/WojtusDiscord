using DiscordEventService.Services.Conversation;
using DSharpPlus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscordEventService.Tests;

// Regression guard for the §6 boot deadlock. Registering DiscordClient in the DSharpPlus child
// container forwards into the very container the client owns, so resolving it re-enters the
// client's own construction during builder.Build() and hangs boot before any log line — which is
// why it slipped past CI (VALIDATE_AND_EXIT could race past it) and only surfaced as an unhealthy,
// silent prod container. The action services must reach the client via DiscordClientAccessor (a
// plain holder set after the client is built), never by registering DiscordClient in a container.
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
}
