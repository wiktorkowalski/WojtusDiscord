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
}
