using System.Net;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using DiscordEventService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The persisted half of #320: the DbUnreachable row a recovered outage writes, and the
// crash-loop alert NOT counting those rows as restarts.
public sealed class DowntimeDbUnreachableTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.BotDowntimeIntervals.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RecordDbUnreachableAsync_WritesOneClosedIntervalCoveringTheWholeWindow()
    {
        var start = DateTime.UtcNow.AddMinutes(-8);
        var end = DateTime.UtcNow;
        var tracker = new DowntimeTrackerService(_db, NullLogger<DowntimeTrackerService>.Instance);

        var id = await tracker.RecordDbUnreachableAsync(new UnwritableWindow(start, end, 96));

        var row = await NewContext().BotDowntimeIntervals.SingleAsync(x => x.Id == id);
        Assert.Equal(BotDowntimeType.DbUnreachable, row.Type);
        Assert.Equal(BotDowntimeDetectionMethod.HeartbeatWriteFailure, row.DetectionMethod);
        Assert.Equal(start, row.StartedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(end, row.EndedAtUtc!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Contains("96 ticks", row.Notes);
    }

    [Fact]
    public async Task CrashLoopAlert_IgnoresDbUnreachableIntervals_ButStillFiresOnRealRestarts()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            _db.BotDowntimeIntervals.Add(Interval(BotDowntimeType.DbUnreachable, now.AddMinutes(-20 + i)));
        await _db.SaveChangesAsync();

        var handler = new CapturingHandler();
        await using var provider = new ServiceCollection()
            .AddDbContext<DiscordDbContext>(o => o
                .UseNpgsql(fixture.ConnectionString)
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider();
        var job = new HealthCheckJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubHttpClientFactory(handler),
            Options.Create(new HealthCheckOptions { WebhookUrl = "https://example.test/webhook" }),
            NullLogger<HealthCheckJob>.Instance);

        // Three surviving-process outages in the window — not a crash-loop.
        await job.ExecuteAsync(CancellationToken.None);
        Assert.DoesNotContain(handler.Bodies, body => body.Contains("crash-loop"));

        for (var i = 0; i < 3; i++)
            _db.BotDowntimeIntervals.Add(Interval(BotDowntimeType.GatewayDisconnect, now.AddMinutes(-10 + i)));
        await _db.SaveChangesAsync();

        // Three real restarts — the alert must still fire (the exclusion is type-scoped).
        await job.ExecuteAsync(CancellationToken.None);
        Assert.Contains(handler.Bodies, body => body.Contains("crash-loop"));
    }

    private static BotDowntimeIntervalEntity Interval(BotDowntimeType type, DateTime startedAtUtc) => new()
    {
        StartedAtUtc = startedAtUtc,
        EndedAtUtc = startedAtUtc.AddSeconds(30),
        Type = type,
        DetectionMethod = BotDowntimeDetectionMethod.HeartbeatWriteFailure,
    };

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient(handler, disposeHandler: false);
    }
}
