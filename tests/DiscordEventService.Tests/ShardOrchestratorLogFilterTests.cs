using DiscordEventService.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// The deploy-time "gateway exited ... UserRequested" Error is dropped only while the host stops.
public sealed class ShardOrchestratorLogFilterTests
{
    [Theory]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void WhileRunning_KeepsInformationAndAbove(LogLevel level) =>
        Assert.True(DiscordClientRegistration.IsShardOrchestratorLogKept(level, hostStopping: false));

    [Fact]
    public void WhileRunning_DropsDebug() =>
        Assert.False(DiscordClientRegistration.IsShardOrchestratorLogKept(LogLevel.Debug, hostStopping: false));

    [Theory]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void WhileStopping_DropsEverything(LogLevel level) =>
        Assert.False(DiscordClientRegistration.IsShardOrchestratorLogKept(level, hostStopping: true));

    [Fact]
    public void FilterApplies_ToTheCategoryDSharpPlusLogsUnder()
    {
        var stopping = false;
        using var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddFilter(DiscordClientRegistration.ShardOrchestratorCategory,
                level => DiscordClientRegistration.IsShardOrchestratorLogKept(level, stopping));
            b.AddProvider(new NullProviderStub());
        });
        var orchestrator = factory.CreateLogger(DiscordClientRegistration.ShardOrchestratorCategory);
        var other = factory.CreateLogger("DSharpPlus.Other");

        Assert.True(orchestrator.IsEnabled(LogLevel.Error));
        stopping = true;
        Assert.False(orchestrator.IsEnabled(LogLevel.Error));
        Assert.True(other.IsEnabled(LogLevel.Error));
    }

    private sealed class NullProviderStub : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Stub();
        public void Dispose() { }

        private sealed class Stub : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            { }
        }
    }
}
