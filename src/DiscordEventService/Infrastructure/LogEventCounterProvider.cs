namespace DiscordEventService.Infrastructure;

// Counts log events by level into BotMetrics, and does nothing else. Registered twice: on the
// root logging builder and in the DSharpPlus child container, which has its own ILoggerFactory
// (EventPipeline and the event handlers log through that one). The logging filter runs before
// a provider, so only events that pass the configured level are counted.
internal sealed class LogEventCounterProvider : ILoggerProvider
{
    private static readonly CountingLogger Logger = new();

    public ILogger CreateLogger(string categoryName) => Logger;

    public void Dispose()
    {
    }

    private sealed class CountingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            BotMetrics.LogEventWritten(logLevel);
    }
}
