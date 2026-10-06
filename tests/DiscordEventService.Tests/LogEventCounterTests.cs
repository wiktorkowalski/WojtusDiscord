using DiscordEventService.Infrastructure;
using DiscordEventService.Services;
using DSharpPlus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// The level tag is not unique to a test, so both tests count Critical, which nothing else in
// the suite logs through a factory that carries the provider. Tests of one class run in turn.
public sealed class LogEventCounterTests
{
    private const string LogEvents = "wojtus.log.events";

    [Fact]
    public void Provider_CountsOnlyTheEventsThatPassTheLevelFilter()
    {
        using var factory = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Critical)
            .AddProvider(new LogEventCounterProvider()));
        var logger = factory.CreateLogger("LogEventCounterTests");
        using var metrics = new MetricsCapture();

        logger.LogError("below the filter, not counted");
        logger.LogCritical("counted");
        logger.LogCritical("counted");

        Assert.Equal(2, metrics.Of(LogEvents, "level", "critical").Sum(m => m.Value));
        Assert.Empty(metrics.Of(LogEvents, "level", "error"));
    }

    // EventPipeline and the handlers log through the DSharpPlus child container's own
    // ILoggerFactory. This builds the production client registration (no gateway connect) and
    // logs the way a handler does.
    [Fact]
    public void ChildContainer_CountsAHandlerSideLogLine()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Discord:Token"] = new string('x', 60) })
            .Build();
        var rootServices = new ServiceCollection();
        rootServices.AddDiscordClient(configuration, new TestHostEnvironment(), _ => { });

        // Not disposed: disposing a client that never connected throws inside DSharpPlus.
        var root = rootServices.BuildServiceProvider();

        var client = root.GetRequiredService<DiscordClient>();
        var handlerLogger = client.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MessageEventHandler");
        using var metrics = new MetricsCapture();

        handlerLogger.LogCritical("a handler-side log line");

        Assert.Equal(1, metrics.Of(LogEvents, "level", "critical").Sum(m => m.Value));
    }
}
