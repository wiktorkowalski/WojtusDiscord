using DiscordEventService.Data;
using DiscordEventService.Services;
using DiscordEventService.Services.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// The event pipeline's metric contract: one outcome per event, and one failure count per
// failure handed to FailedEventService — a hard failure and a soft one (FkResolver) alike.
public sealed class EventPipelineMetricsTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Events = "wojtus.events";
    private const string Failures = "wojtus.event.failures";

    private sealed record FakeEventArgs(string Text);

    [Fact]
    public async Task HandledEvent_CountsOneOkOutcomeWithItsDurationAndRawSize()
    {
        var eventType = NewEventType();
        await using var services = BuildServices();
        using var metrics = new MetricsCapture();

        await ExecuteAsync(services, eventType, _ => Task.CompletedTask);

        var counted = Assert.Single(metrics.Of(Events, "event_type", eventType));
        Assert.Equal("ok", counted.Tags["outcome"]);
        Assert.Equal(1, counted.Value);
        Assert.Single(metrics.Of("wojtus.event.handler.duration", "event_type", eventType));
        Assert.True(Assert.Single(metrics.Of("wojtus.event.raw.size", "event_type", eventType)).Value > 0);
        Assert.Empty(metrics.Of(Failures, "event_type", eventType));
    }

    [Fact]
    public async Task ThrowingHandler_CountsOneFailedOutcomeAndOneFailure()
    {
        var eventType = NewEventType();
        await using var services = BuildServices();
        using var metrics = new MetricsCapture();

        await ExecuteAsync(services, eventType, _ => throw new InvalidOperationException("boom"));

        Assert.Equal("failed", Assert.Single(metrics.Of(Events, "event_type", eventType)).Tags["outcome"]);
        var failure = Assert.Single(metrics.Of(Failures, "event_type", eventType));
        Assert.Equal("MetricsTestHandler", failure.Tags["handler"]);

        // The counter and the failed_events table agree.
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        Assert.Equal(1, await db.FailedEvents.CountAsync(f => f.EventType == eventType));
    }

    [Fact]
    public async Task SoftFailures_CountOneFailedOutcomeAndEveryFailure()
    {
        var eventType = NewEventType();
        await using var services = BuildServices();
        using var metrics = new MetricsCapture();

        // What FkResolver does: record the failure and return, with no exception.
        await ExecuteAsync(services, eventType, async ctx =>
        {
            await ctx.RecordFailureAsync(new InvalidOperationException("guild FK not resolved"));
            await ctx.RecordFailureAsync(new InvalidOperationException("user FK not resolved"));
        });

        Assert.Equal("failed", Assert.Single(metrics.Of(Events, "event_type", eventType)).Tags["outcome"]);
        Assert.Equal(2, metrics.Of(Failures, "event_type", eventType).Count);
    }

    [Fact]
    public async Task HandledEvent_MovesTheLastEventGaugesToNow()
    {
        await using var services = BuildServices();
        using var metrics = new MetricsCapture();
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // "Voice..." is the prefix of the voice kind; no other test of the suite sends one.
        await ExecuteAsync(services, $"VoiceMetricsTest-{Guid.NewGuid():N}", _ => Task.CompletedTask);
        metrics.Observe();

        Assert.True(Assert.Single(metrics.Of("wojtus.last_event.timestamp")).Value >= before);
        Assert.True(Assert.Single(metrics.Of("wojtus.last_event.by_kind.timestamp", "kind", "voice")).Value >= before);
    }

    private static string NewEventType() => $"MetricsTest-{Guid.NewGuid():N}";

    private static Task ExecuteAsync(ServiceProvider services, string eventType, Func<EventContext, Task> handler) =>
        services.GetRequiredService<EventPipeline>().ExecuteAsync(
            new FakeEventArgs("payload"), eventType, "MetricsTestHandler",
            guildId: 1, channelId: 2, userId: 3, handler);

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddScoped<RawEventLogService>();
        services.AddScoped<FailedEventService>();
        services.AddSingleton<EventPipeline>();
        return services.BuildServiceProvider();
    }
}
