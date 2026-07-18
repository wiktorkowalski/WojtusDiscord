using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// #282: a checkpoint left InProgress by a hard kill must not starve automatic backfill
// forever — the periodic sweep treats an InProgress row with a stale heartbeat as dead.
public sealed class BackfillStaleCheckpointTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildId = 777_000_001UL;

    private ServiceProvider _provider = null!;
    private RecordingJobClient _jobClient = null!;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.BackfillCheckpoints.ExecuteDeleteAsync();
        await db.Guilds.ExecuteDeleteAsync();

        db.Guilds.Add(new GuildEntity { DiscordId = GuildId, Name = "stale-test-guild", OwnerId = 1UL });
        db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildId,
            Type = BackfillType.Messages,
            Status = BackfillStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _jobClient = new RecordingJobClient();
        _provider = new ServiceCollection()
            .AddDbContext<DiscordDbContext>(o => o
                .UseNpgsql(fixture.Container.GetConnectionString())
                .UseSnakeCaseNamingConvention())
            .AddSingleton<IBackgroundJobClient>(_jobClient)
            .AddScoped<GuildBackfillOrchestrator>()
            .AddLogging()
            .BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task ExecuteAsync_FreshInProgressCheckpoint_SkipsGuild()
    {
        await RunPeriodicJobAsync();

        Assert.Empty(_jobClient.Created);
    }

    [Fact]
    public async Task ExecuteAsync_StaleInProgressCheckpoint_EnqueuesChain()
    {
        await AgeCheckpointHeartbeatAsync(BackfillCheckpointEntity.StaleInProgressAfter + TimeSpan.FromMinutes(5));

        await RunPeriodicJobAsync();

        Assert.NotEmpty(_jobClient.Created);
        Assert.Contains(_jobClient.Created, j => j.Type == typeof(RolesBackfillJob));
    }

    [Theory]
    [InlineData(BackfillStatus.Pending)]
    [InlineData(BackfillStatus.Completed)]
    [InlineData(BackfillStatus.Failed)]
    [InlineData(BackfillStatus.Cancelled)]
    public void IsActivelyInProgress_IsFalse_ForTerminalStatuses(BackfillStatus status)
    {
        var checkpoint = new BackfillCheckpointEntity { Status = status, LastUpdatedUtc = DateTime.UtcNow };

        Assert.False(checkpoint.IsActivelyInProgress(DateTime.UtcNow));
    }

    [Fact]
    public void IsActivelyInProgress_FlipsAtTheStaleThreshold()
    {
        var now = DateTime.UtcNow;
        var fresh = new BackfillCheckpointEntity
        {
            Status = BackfillStatus.InProgress,
            LastUpdatedUtc = now - BackfillCheckpointEntity.StaleInProgressAfter + TimeSpan.FromMinutes(1)
        };
        var stale = new BackfillCheckpointEntity
        {
            Status = BackfillStatus.InProgress,
            LastUpdatedUtc = now - BackfillCheckpointEntity.StaleInProgressAfter - TimeSpan.FromMinutes(1)
        };

        Assert.True(fresh.IsActivelyInProgress(now));
        Assert.False(stale.IsActivelyInProgress(now));
    }

    private async Task RunPeriodicJobAsync()
    {
        var job = new PeriodicFullBackfillJob(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PeriodicFullBackfillJob>.Instance);
        await job.ExecuteAsync();
    }

    // ExecuteUpdate bypasses SaveChangesAsync's UpdateTimestamps, so the heartbeat
    // can be backdated the same way a dead process leaves it frozen.
    private async Task AgeCheckpointHeartbeatAsync(TimeSpan age)
    {
        await using var db = NewContext();
        var frozenAt = DateTime.UtcNow - age;
        await db.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == GuildId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUpdatedUtc, frozenAt));
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
