using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// #288: a Pending/InProgress checkpoint at boot belongs to a dead run — the sweep flips it to
// Failed and deletes its recorded Hangfire job so the orphan cannot re-run after the sliding
// invisibility timeout and interleave with the reconnect chain.
public sealed class StartupBackfillSweepTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildId = 999_000_001UL;

    private ServiceProvider _provider = null!;
    private RecordingJobClient _jobClient = null!;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.BackfillCheckpoints.ExecuteDeleteAsync();

        _jobClient = new RecordingJobClient();
        _provider = new ServiceCollection()
            .AddDbContext<DiscordDbContext>(o => o
                .UseNpgsql(fixture.Container.GetConnectionString())
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Sweep_FailsDeadCheckpoints_AndDeletesTheirJobs()
    {
        await SeedAsync(
            Checkpoint(BackfillType.Messages, BackfillStatus.InProgress, "job-messages"),
            Checkpoint(BackfillType.Reactions, BackfillStatus.Pending, "job-reactions"),
            Checkpoint(BackfillType.MemeIndex, BackfillStatus.InProgress, "job-meme"));

        await RunSweepAsync();

        await using var db = NewContext();
        var rows = await db.BackfillCheckpoints.Where(c => c.GuildDiscordId == GuildId).ToListAsync();
        Assert.All(rows, c =>
        {
            Assert.Equal(BackfillStatus.Failed, c.Status);
            Assert.Equal(1, c.ErrorCount);
            Assert.Contains("restart", c.LastError);
            Assert.NotNull(c.LastErrorAtUtc);
        });

        var deleted = _jobClient.StateChanges.Where(s => s.State == "Deleted").Select(s => s.JobId).ToList();
        Assert.Equal(["job-meme", "job-messages", "job-reactions"], deleted.Order());
    }

    [Fact]
    public async Task Sweep_LegacyRowWithoutJobId_StillFailsCheckpoint()
    {
        await SeedAsync(Checkpoint(BackfillType.Messages, BackfillStatus.InProgress, hangfireJobId: null));

        await RunSweepAsync();

        await using var db = NewContext();
        var row = await db.BackfillCheckpoints.SingleAsync(c => c.GuildDiscordId == GuildId);
        Assert.Equal(BackfillStatus.Failed, row.Status);
        Assert.Empty(_jobClient.StateChanges);
    }

    // Live-caught on first boot: Hangfire's Delete throws when the job id no longer exists in
    // storage; the sweep must still fail the checkpoint instead of crashing startup.
    [Fact]
    public async Task Sweep_JobDeleteThrows_StillFailsCheckpoint()
    {
        await SeedAsync(Checkpoint(BackfillType.Messages, BackfillStatus.InProgress, "gone-job"));

        var sweep = new StartupBackfillSweep(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new ThrowingJobClient(),
            NullLogger<StartupBackfillSweep>.Instance);
        await sweep.SweepAsync();

        await using var db = NewContext();
        var row = await db.BackfillCheckpoints.SingleAsync(c => c.GuildDiscordId == GuildId);
        Assert.Equal(BackfillStatus.Failed, row.Status);
    }

    [Fact]
    public async Task Sweep_TerminalCheckpoints_AreUntouched()
    {
        await SeedAsync(
            Checkpoint(BackfillType.Roles, BackfillStatus.Completed, "job-roles"),
            Checkpoint(BackfillType.Messages, BackfillStatus.Failed, "job-messages"),
            Checkpoint(BackfillType.Reactions, BackfillStatus.Cancelled, "job-reactions"));

        await RunSweepAsync();

        await using var db = NewContext();
        var rows = await db.BackfillCheckpoints.Where(c => c.GuildDiscordId == GuildId).ToListAsync();
        Assert.All(rows, c => Assert.Equal(0, c.ErrorCount));
        Assert.Equal(BackfillStatus.Completed,
            rows.Single(c => c.Type == BackfillType.Roles).Status);
        Assert.Empty(_jobClient.StateChanges);
    }

    private static BackfillCheckpointEntity Checkpoint(
        BackfillType type, BackfillStatus status, string? hangfireJobId) => new()
        {
            GuildDiscordId = GuildId,
            Type = type,
            Status = status,
            HangfireJobId = hangfireJobId,
            StartedAtUtc = DateTime.UtcNow
        };

    private async Task SeedAsync(params BackfillCheckpointEntity[] checkpoints)
    {
        await using var db = NewContext();
        db.BackfillCheckpoints.AddRange(checkpoints);
        await db.SaveChangesAsync();
    }

    private async Task RunSweepAsync()
    {
        var sweep = new StartupBackfillSweep(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _jobClient,
            NullLogger<StartupBackfillSweep>.Instance);
        await sweep.SweepAsync();
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

internal sealed class ThrowingJobClient : Hangfire.IBackgroundJobClient
{
    public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => "1";

    public bool ChangeState(string jobId, Hangfire.States.IState state, string expectedState)
        => throw new Hangfire.BackgroundJobClientException("state change failed", new FormatException(jobId));
}
