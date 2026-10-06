using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// #400: what the boot leaves in bot_downtime_intervals for the restart it follows. A deploy
// that recreates Postgres kills the DB before the bot, so StopAsync cannot write its row —
// and the boot used to infer one only from a gap of 30s, which a 20-24s restart never reached.
public sealed class DowntimeStartupSettleTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly List<DiscordDbContext> _contexts = [];
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.BotDowntimeIntervals.ExecuteDeleteAsync();
        await _db.BotHeartbeats.ExecuteDeleteAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.DisposeAsync();
    }

    [Fact]
    public async Task SettlePriorSessionAsync_WritesAClosedRowForAGapUnderThirtySeconds_WhenTheStopPathWroteNone()
    {
        // The prod signature: no open row, heartbeats that stopped 22s before the boot.
        var boot = DateTime.UtcNow;
        var lastHeartbeat = boot.AddSeconds(-22);
        await AddHeartbeatAsync(lastHeartbeat);

        await NewTracker().SettlePriorSessionAsync();

        var row = await NewContext().BotDowntimeIntervals.SingleAsync();
        Assert.Equal(BotDowntimeType.Inferred, row.Type);
        Assert.Equal(BotDowntimeDetectionMethod.StartupGapInference, row.DetectionMethod);
        Assert.Equal(lastHeartbeat, row.StartedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(row.EndedAtUtc);
        Assert.InRange(row.EndedAtUtc!.Value, boot, DateTime.UtcNow);

        // The reconnect backfill sizes itself from the same instant as before the row
        // existed, so writing it cannot turn a quick-sync into a crawl.
        var resolved = await NewTracker().ResolveGapStartAsync(boot);
        Assert.Equal(lastHeartbeat, resolved!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task SettlePriorSessionAsync_ClosesTheStopPathRowAndWritesNoSecondOne()
    {
        // A normal deploy: StopAsync opened its row 4s ago, one heartbeat before that.
        var boot = DateTime.UtcNow;
        var stoppedAt = boot.AddSeconds(-4);
        await AddHeartbeatAsync(boot.AddSeconds(-6));
        _db.BotDowntimeIntervals.Add(new BotDowntimeIntervalEntity
        {
            StartedAtUtc = stoppedAt,
            EndedAtUtc = null,
            Type = BotDowntimeType.Deploy,
            DetectionMethod = BotDowntimeDetectionMethod.GracefulStop
        });
        await _db.SaveChangesAsync();

        await NewTracker().SettlePriorSessionAsync();

        var row = await NewContext().BotDowntimeIntervals.SingleAsync();
        Assert.Equal(BotDowntimeType.Deploy, row.Type);
        Assert.Equal(stoppedAt, row.StartedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(row.EndedAtUtc);
        Assert.InRange(row.EndedAtUtc!.Value, boot, DateTime.UtcNow);
    }

    [Fact]
    public async Task SettlePriorSessionAsync_WritesNothingOnTheVeryFirstRun()
    {
        await NewTracker().SettlePriorSessionAsync();

        Assert.Empty(await NewContext().BotDowntimeIntervals.ToListAsync());
    }

    [Fact]
    public async Task SettlePriorSessionAsync_StillInfersALongGap()
    {
        var boot = DateTime.UtcNow;
        var lastHeartbeat = boot.AddMinutes(-2);
        await AddHeartbeatAsync(lastHeartbeat);

        await NewTracker().SettlePriorSessionAsync();

        var row = await NewContext().BotDowntimeIntervals.SingleAsync();
        Assert.Equal(BotDowntimeType.Inferred, row.Type);
        Assert.Equal(BotDowntimeDetectionMethod.StartupGapInference, row.DetectionMethod);
        Assert.Equal(lastHeartbeat, row.StartedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.InRange(row.EndedAtUtc!.Value, boot, DateTime.UtcNow);
    }

    private async Task AddHeartbeatAsync(DateTime atUtc)
    {
        // IsGatewayConnected must be true: GetLastAliveAtUtcAsync excludes anything else, so
        // an in-process gateway drop does not read as "alive".
        _db.BotHeartbeats.Add(new BotHeartbeatEntity { LastHeartbeatUtc = atUtc, IsGatewayConnected = true });
        await _db.SaveChangesAsync();
    }

    // A fresh context per call, so the read cannot be served from the tracked graph.
    private DowntimeTrackerService NewTracker() =>
        new(NewContext(), NullLogger<DowntimeTrackerService>.Instance);

    private DiscordDbContext NewContext()
    {
        var context = new DiscordDbContext(new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
        _contexts.Add(context);
        return context;
    }
}
