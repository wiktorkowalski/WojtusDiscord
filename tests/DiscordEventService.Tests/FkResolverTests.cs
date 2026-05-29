using DiscordEventService.Services;
using DiscordEventService.Services.Pipeline;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class FkResolverTests
{
    [Fact]
    public async Task ValidateAsync_WhenAllResolve_ReturnsResolvedIdsAndDoesNotRecordFailure()
    {
        var guildId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var logger = new RecordingLogger();
        var recordedFailures = new List<Exception>();
        var ctx = NewContext(logger, recordedFailures);

        var result = await FkResolver.ValidateAsync(
            ctx,
            UpsertResult<Guid>.Success(guildId),
            UpsertResult<Guid>.Success(channelId),
            UpsertResult<Guid>.Success(userId),
            "MessageId=1");

        Assert.True(result.Success);
        Assert.Equal(guildId, result.GuildId);
        Assert.Equal(channelId, result.ChannelId);
        Assert.Equal(userId, result.UserId);
        Assert.Empty(recordedFailures);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task ValidateAsync_WhenAnyFkFails_LogsRecordsFailureAndReturnsFailed(
        bool guildOk, bool channelOk, bool userOk)
    {
        var logger = new RecordingLogger();
        var recordedFailures = new List<Exception>();
        var ctx = NewContext(logger, recordedFailures);

        var result = await FkResolver.ValidateAsync(
            ctx,
            guildOk ? UpsertResult<Guid>.Success(Guid.NewGuid()) : UpsertResult<Guid>.Failure("guild lost"),
            channelOk ? UpsertResult<Guid>.Success(Guid.NewGuid()) : UpsertResult<Guid>.Failure("channel lost"),
            userOk ? UpsertResult<Guid>.Success(Guid.NewGuid()) : UpsertResult<Guid>.Failure("user lost"),
            "MessageId=42");

        Assert.False(result.Success);
        Assert.Equal(Guid.Empty, result.GuildId);
        Assert.Equal(Guid.Empty, result.ChannelId);
        Assert.Equal(Guid.Empty, result.UserId);

        // Logged an aggregate error and recorded exactly one FailedEvent.
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
        var failure = Assert.Single(recordedFailures);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("MessageId=42", failure.Message);
    }

    private static EventContext NewContext(ILogger logger, List<Exception> recordedFailures) =>
        new(
            Db: null!,
            Services: null!,
            CorrelationId: Guid.NewGuid(),
            RawJson: null,
            ReceivedAtUtc: DateTime.UnixEpoch,
            Logger: logger,
            RecordFailureAsync: ex =>
            {
                recordedFailures.Add(ex);
                return Task.CompletedTask;
            });

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
