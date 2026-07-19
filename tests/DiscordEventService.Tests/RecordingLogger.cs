using Microsoft.Extensions.Logging;

namespace DiscordEventService.Tests;

// Shared capture logger for service contract tests: records (level, message) pairs so tests
// can assert on warning/error paths without a mocking framework.
internal sealed class RecordingLogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger<T> For<T>() => new TypedLogger<T>(Entries);

    private sealed class TypedLogger<T>(List<(LogLevel, string)> entries) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Add((logLevel, formatter(state, exception)));
    }
}
