using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// #350: which timestamp a reconnect backfill sizes itself from. A deploy that recreates
// Postgres kills the DB before the bot, so StopAsync cannot write its shutdown row — and the
// resolution used to reach back to whatever row was closed last, however old, turning every
// deploy into a 2-day crawl.
public sealed class DowntimeGapResolutionTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.BotDowntimeIntervals.ExecuteDeleteAsync();
        await _db.BotHeartbeats.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ResolveGapStartAsync_IgnoresAClosedRowFromAnEarlierProcess()
    {
        // The prod signature: a 33-day-old closed row, no row for this boot, and heartbeats
        // that stopped 22s before it. The gap is 22s, not 33 days.
        var boot = DateTime.UtcNow;
        var lastHeartbeat = boot.AddSeconds(-22);
        await AddClosedIntervalAsync(startedAt: boot.AddDays(-33).AddMinutes(-5), endedAt: boot.AddDays(-33));
        await AddHeartbeatAsync(lastHeartbeat);

        var resolved = await NewTracker().ResolveGapStartAsync(boot);

        Assert.NotNull(resolved);
        Assert.Equal(lastHeartbeat, resolved!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ResolveGapStartAsync_PrefersARowClosedJustAfterBootOverHeartbeats()
    {
        // The boundary case. A shutdown row is closed from StartAsync, milliseconds after
        // boot — it must still win over the heartbeat fallback, or a legitimate row gets
        // dropped and the bug returns intermittently.
        var boot = DateTime.UtcNow;
        var shutdownStartedAt = boot.AddMinutes(-3);
        await AddClosedIntervalAsync(startedAt: shutdownStartedAt, endedAt: boot.AddMilliseconds(40));
        await AddHeartbeatAsync(boot.AddSeconds(-5));

        var resolved = await NewTracker().ResolveGapStartAsync(boot);

        Assert.Equal(shutdownStartedAt, resolved!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ResolveGapStartAsync_IgnoresSignalsWrittenAfterBoot()
    {
        // Post-reconnect pollution: by the time this runs the gateway is back, so fresh
        // heartbeats exist. Counting them would report a seconds-long gap however long the
        // bot was actually down.
        var boot = DateTime.UtcNow;
        var lastPreBootHeartbeat = boot.AddHours(-6);
        await AddHeartbeatAsync(lastPreBootHeartbeat);
        await AddHeartbeatAsync(boot.AddSeconds(3));
        await AddHeartbeatAsync(boot.AddSeconds(8));

        var resolved = await NewTracker().ResolveGapStartAsync(boot);

        Assert.Equal(lastPreBootHeartbeat, resolved!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ResolveGapStartAsync_ReturnsNullOnTheVeryFirstRun()
    {
        Assert.Null(await NewTracker().ResolveGapStartAsync(DateTime.UtcNow));
    }

    private async Task AddClosedIntervalAsync(DateTime startedAt, DateTime endedAt)
    {
        _db.BotDowntimeIntervals.Add(new BotDowntimeIntervalEntity
        {
            StartedAtUtc = startedAt,
            EndedAtUtc = endedAt,
            Type = BotDowntimeType.Deploy,
            DetectionMethod = BotDowntimeDetectionMethod.GracefulStop
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddHeartbeatAsync(DateTime atUtc)
    {
        // IsGatewayConnected must be true: GetLastAliveAtUtcAsync excludes anything else, so
        // an in-process gateway drop does not read as "alive".
        _db.BotHeartbeats.Add(new BotHeartbeatEntity { LastHeartbeatUtc = atUtc, IsGatewayConnected = true });
        await _db.SaveChangesAsync();
    }

    // A fresh context per assertion, so the read cannot be served from the tracked graph.
    private DowntimeTrackerService NewTracker() =>
        new(NewContext(), NullLogger<DowntimeTrackerService>.Instance);

    private DiscordDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
