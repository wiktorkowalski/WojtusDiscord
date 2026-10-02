using System.Net;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// #345: the per-type silence check and the data-integrity checks. Each alert is an episode: it fires
// once and stays quiet while the same condition persists, which is what the night alerts lacked.
[Collection("HealthCheckJobStatics")]
public sealed class HealthCheckSilenceAndIntegrityTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        await _db.BotHeartbeats.ExecuteDeleteAsync();
        await _db.BackfillCheckpoints.ExecuteDeleteAsync();
        await _db.BotDowntimeIntervals.ExecuteDeleteAsync();
        await _db.MessageEvents.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ExecuteAsync_MessageCreatedSilentPastThreshold_AlertsOncePerEpisodeAndIgnoresVoice()
    {
        var now = DateTime.UtcNow;
        await SeedFreshGatewayAsync(now);
        AddRawEvent("MessageCreated", now.AddHours(-50));
        // Voice silent far longer, but it is not in EventSilenceHours, so it must never alert.
        AddRawEvent("VoiceStateUpdated", now.AddHours(-300));
        await _db.SaveChangesAsync();

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Event silence", body);
        Assert.Contains("MessageCreated", body);
        Assert.DoesNotContain("VoiceStateUpdated", body);

        // A new message ends the episode; the next silence past the threshold is a new alert.
        AddRawEvent("MessageCreated", now.AddHours(-49));
        await _db.SaveChangesAsync();
        await job.ExecuteAsync(CancellationToken.None);
        Assert.Equal(2, handler.Bodies.Count(b => b.Contains("Event silence")));
    }

    [Fact]
    public async Task ExecuteAsync_MessageCreatedSilentUnderThreshold_StaysQuiet()
    {
        var now = DateTime.UtcNow;
        await SeedFreshGatewayAsync(now);
        AddRawEvent("MessageCreated", now.AddHours(-20));
        await _db.SaveChangesAsync();

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task ExecuteAsync_SilenceWithGatewayDown_StaysQuiet()
    {
        // No heartbeat: silence while disconnected is downtime, covered by the downtime tracker.
        AddRawEvent("MessageCreated", DateTime.UtcNow.AddHours(-60));
        await _db.SaveChangesAsync();

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task ExecuteAsync_InProgressCheckpointWithoutProgress_AlertsOnce()
    {
        var now = DateTime.UtcNow;
        var checkpoint = new BackfillCheckpointEntity
        {
            Id = Guid.NewGuid(),
            GuildDiscordId = 42,
            Type = BackfillType.Messages,
            Status = BackfillStatus.InProgress,
            StartedAtUtc = now.AddHours(-8),
        };
        _db.BackfillCheckpoints.Add(checkpoint);
        await _db.SaveChangesAsync();
        // SaveChanges stamps LastUpdatedUtc with the current time, so age it after the insert.
        await _db.BackfillCheckpoints.Where(c => c.Id == checkpoint.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUpdatedUtc, now.AddHours(-7)));

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Backfill stalled", body);
        Assert.Contains("Messages", body);
    }

    [Fact]
    public async Task ExecuteAsync_OpenDowntimeRowWithGatewayConnected_AlertsOnce()
    {
        var now = DateTime.UtcNow;
        await SeedFreshGatewayAsync(now);
        _db.BotDowntimeIntervals.Add(new BotDowntimeIntervalEntity
        {
            Id = Guid.NewGuid(),
            StartedAtUtc = now.AddHours(-2),
            Type = BotDowntimeType.Deploy,
            DetectionMethod = BotDowntimeDetectionMethod.Manual,
        });
        await _db.SaveChangesAsync();

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Downtime row left open", body);
    }

    [Fact]
    public async Task ExecuteAsync_OpenDowntimeRowWithGatewayDown_StaysQuiet()
    {
        _db.BotDowntimeIntervals.Add(new BotDowntimeIntervalEntity
        {
            Id = Guid.NewGuid(),
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            Type = BotDowntimeType.GatewayDisconnect,
            DetectionMethod = BotDowntimeDetectionMethod.GatewayEvent,
        });
        await _db.SaveChangesAsync();

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task ExecuteAsync_CreatedMessageWithReceiveTimeAsEventTime_AlertsOnce()
    {
        var now = DateTime.UtcNow;
        AddMessageEvent(MessageEventType.Created, eventTime: now.AddMinutes(-5), receivedAt: now.AddMinutes(-5));
        // Discord-timestamped rows and non-Created rows are fine.
        AddMessageEvent(MessageEventType.Created, eventTime: now.AddMinutes(-6), receivedAt: now.AddMinutes(-5));
        AddMessageEvent(MessageEventType.Updated, eventTime: now.AddMinutes(-4), receivedAt: now.AddMinutes(-4));
        await _db.SaveChangesAsync();

        var (job, handler) = NewJob();
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Data integrity", body);
        // The webhook body is JSON, and System.Text.Json escapes backticks.
        Assert.Contains("1 \\u0060MessageCreated\\u0060 row(s)", body);
    }

    private async Task SeedFreshGatewayAsync(DateTime now)
    {
        _db.BotHeartbeats.Add(new BotHeartbeatEntity { Id = Guid.NewGuid(), LastHeartbeatUtc = now, IsGatewayConnected = true });
        // A fresh event keeps the ingest-stall check quiet, so only the check under test can alert.
        AddRawEvent("PresenceUpdated", now);
        await _db.SaveChangesAsync();
    }

    private void AddRawEvent(string eventType, DateTime receivedAt)
        => _db.RawEventLogs.Add(new RawEventLogEntity
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            EventJson = "{}",
            ReceivedAtUtc = receivedAt,
        });

    private void AddMessageEvent(MessageEventType type, DateTime eventTime, DateTime receivedAt)
        => _db.MessageEvents.Add(new MessageEventEntity
        {
            Id = Guid.NewGuid(),
            MessageDiscordId = (ulong)Random.Shared.NextInt64(1, long.MaxValue),
            EventType = type,
            EventTimestampUtc = eventTime,
            ReceivedAtUtc = receivedAt,
        });

    private (HealthCheckJob Job, CapturingHandler Handler) NewJob()
    {
        var handler = new CapturingHandler();
        var provider = new ServiceCollection()
            .AddDbContext<DiscordDbContext>(o => o
                .UseNpgsql(fixture.ConnectionString)
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider();

        var job = new HealthCheckJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubHttpClientFactory(handler),
            Options.Create(new HealthCheckOptions { WebhookUrl = "https://example.test/webhook" }),
            NullLogger<HealthCheckJob>.Instance);
        return (job, handler);
    }

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
