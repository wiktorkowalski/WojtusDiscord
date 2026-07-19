using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// #289: the orchestrator is the single atomic guard against two chains running for the same
// guild — the active-chain check and the enqueue happen under a per-guild advisory lock, and
// every chain job's Hangfire id lands on its checkpoint row before the job runs.
public sealed class BackfillChainGuardTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildId = 888_000_001UL;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.BackfillCheckpoints.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnqueueChain_RecordsPendingCheckpointWithJobId_ForEveryChainType()
    {
        await using var db = NewContext();
        var jobClient = new RecordingJobClient();
        var orchestrator = NewOrchestrator(db, jobClient);

        var jobId = await orchestrator.StartBackfillAsync(GuildId);

        Assert.NotNull(jobId);
        Assert.Equal(7, jobClient.Created.Count);

        await using var verify = NewContext();
        var checkpoints = await verify.BackfillCheckpoints
            .Where(c => c.GuildDiscordId == GuildId)
            .ToListAsync();
        Assert.Equal(7, checkpoints.Count);
        Assert.All(checkpoints, c =>
        {
            Assert.Equal(BackfillStatus.Pending, c.Status);
            Assert.False(string.IsNullOrEmpty(c.HangfireJobId));
        });
    }

    [Fact]
    public async Task EnqueueChain_WhilePendingRowsFresh_IsSkipped()
    {
        await using var db = NewContext();
        var jobClient = new RecordingJobClient();
        var orchestrator = NewOrchestrator(db, jobClient);
        Assert.NotNull(await orchestrator.StartBackfillAsync(GuildId));

        await using var db2 = NewContext();
        var jobClient2 = new RecordingJobClient();
        var second = await NewOrchestrator(db2, jobClient2).StartBackfillAsync(GuildId);

        Assert.Null(second);
        Assert.Empty(jobClient2.Created);
    }

    [Fact]
    public async Task EnqueueChain_OtherGuildChainActive_IsNotBlocked()
    {
        await using var db = NewContext();
        Assert.NotNull(await NewOrchestrator(db, new RecordingJobClient()).StartBackfillAsync(GuildId));

        await using var db2 = NewContext();
        var other = await NewOrchestrator(db2, new RecordingJobClient()).StartBackfillAsync(GuildId + 1);

        Assert.NotNull(other);
    }

    [Fact]
    public async Task EnqueueChain_StaleInProgressRow_EnqueuesAndPreservesResumeStatus()
    {
        await using (var seed = NewContext())
        {
            seed.BackfillCheckpoints.Add(new BackfillCheckpointEntity
            {
                GuildDiscordId = GuildId,
                Type = BackfillType.Messages,
                Status = BackfillStatus.InProgress,
                CurrentChannelId = 42UL,
                StartedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
            var frozenAt = DateTime.UtcNow - BackfillCheckpointEntity.StaleInProgressAfter - TimeSpan.FromMinutes(5);
            await seed.BackfillCheckpoints
                .Where(c => c.GuildDiscordId == GuildId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUpdatedUtc, frozenAt));
        }

        await using var db = NewContext();
        var jobId = await NewOrchestrator(db, new RecordingJobClient()).StartBackfillAsync(GuildId);

        Assert.NotNull(jobId);
        await using var verify = NewContext();
        var messages = await verify.BackfillCheckpoints
            .SingleAsync(c => c.GuildDiscordId == GuildId && c.Type == BackfillType.Messages);
        // Status stays InProgress so the executor's resume branch keeps the cursor (#282/PR #285);
        // the heartbeat bump from the enqueue save re-arms the chain guard.
        Assert.Equal(BackfillStatus.InProgress, messages.Status);
        Assert.Equal(42UL, messages.CurrentChannelId);
        Assert.False(string.IsNullOrEmpty(messages.HangfireJobId));
        Assert.True(messages.IsChainActive(DateTime.UtcNow));
    }

    [Fact]
    public async Task EnqueueChain_ConcurrentTriggers_OnlyOneWins()
    {
        await using var db1 = NewContext();
        await using var db2 = NewContext();
        var client1 = new RecordingJobClient();
        var client2 = new RecordingJobClient();

        var results = await Task.WhenAll(
            NewOrchestrator(db1, client1).StartBackfillAsync(GuildId),
            NewOrchestrator(db2, client2).StartBackfillAsync(GuildId));

        Assert.Single(results, r => r is not null);
        Assert.Equal(7, client1.Created.Count + client2.Created.Count);
    }

    [Fact]
    public async Task EnqueueChain_TerminalCheckpoints_DoNotBlock()
    {
        await using (var seed = NewContext())
        {
            seed.BackfillCheckpoints.Add(new BackfillCheckpointEntity
            {
                GuildDiscordId = GuildId,
                Type = BackfillType.Messages,
                Status = BackfillStatus.Completed,
                CompletedAtUtc = DateTime.UtcNow,
                StartedAtUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var jobId = await NewOrchestrator(db, new RecordingJobClient()).StartBackfillAsync(GuildId);

        Assert.NotNull(jobId);
        await using var verify = NewContext();
        var messages = await verify.BackfillCheckpoints
            .SingleAsync(c => c.GuildDiscordId == GuildId && c.Type == BackfillType.Messages);
        Assert.Equal(BackfillStatus.Pending, messages.Status);
        Assert.Null(messages.CompletedAtUtc);
    }

    private static GuildBackfillOrchestrator NewOrchestrator(DiscordDbContext db, RecordingJobClient jobClient)
        => new(db, jobClient, NullLogger<GuildBackfillOrchestrator>.Instance);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
