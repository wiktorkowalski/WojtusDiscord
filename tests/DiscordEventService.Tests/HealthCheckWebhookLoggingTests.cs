using System.Net;
using DiscordEventService.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// The token of the alert webhook is a path segment of its URL. No log line and no log scope
// of the call may hold it, at any level.
public sealed class HealthCheckWebhookLoggingTests
{
    private const string Token = "s3cret-Token_x";
    private const string WebhookUrl = $"https://discord.com/api/webhooks/123/{Token}";

    [Fact]
    public async Task WebhookClient_Post_WritesNoLogLineAndNoScopeWithTheUrl()
    {
        var logs = await PostAsync(HealthCheckJob.WebhookHttpClientName);

        Assert.DoesNotContain(logs, line => line.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, line => line.Contains("webhooks", StringComparison.OrdinalIgnoreCase));
    }

    // The control: the same call through a client with the default loggers does write the
    // token, so the test above can see a leak.
    [Fact]
    public async Task DefaultClient_Post_WritesTheUrl()
    {
        var logs = await PostAsync(Microsoft.Extensions.Options.Options.DefaultName);

        Assert.Contains(logs, line => line.Contains(Token, StringComparison.Ordinal));
    }

    // Every message and every scope a client writes for one POST, with all levels on.
    private static async Task<List<string>> PostAsync(string clientName)
    {
        var provider = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        services.AddHttpClient();
        services.AddHealthCheckWebhookClient();
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => new NoContentHandler()));

        await using var root = services.BuildServiceProvider();
        var client = root.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        using var response = await client.PostAsync(WebhookUrl, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return provider.Snapshot();
    }

    private sealed class NoContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _lines = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public List<string> Snapshot()
        {
            lock (_lines)
                return [.. _lines];
        }

        public void Dispose()
        {
        }

        private void Add(string? line)
        {
            lock (_lines)
                _lines.Add(line ?? string.Empty);
        }

        private sealed class RecordingLogger(RecordingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Add(state.ToString());
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Add(formatter(state, exception));
        }
    }
}
