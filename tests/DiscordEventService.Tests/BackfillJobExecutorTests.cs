using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class BackfillJobExecutorTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildId = 555_000_001UL;
    private const BackfillType Type = BackfillType.Roles;

    private ServiceProvider _provider = null!;
    private BackfillJobExecutor _executor = null!;

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.BackfillCheckpoints.ExecuteDeleteAsync();

        _provider = new ServiceCollection()
            .AddDbContext<DiscordDbContext>(o => o
                .UseNpgsql(fixture.Container.GetConnectionString())
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider();

        _executor = new BackfillJobExecutor(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BackfillJobExecutor>.Instance);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task RunAsync_WhenWorkCompletes_MarksCompleted()
    {
        await _executor.RunAsync(Type, GuildId, _ => Task.FromResult(BackfillOutcome.Completed), default);

        var row = await ReadCheckpointAsync();
        Assert.Equal(BackfillStatus.Completed, row.Status);
        Assert.NotNull(row.CompletedAtUtc);
    }

    [Fact]
    public async Task RunAsync_WhenWorkShortCircuits_MarksFailedNotCompleted()
    {
        await _executor.RunAsync(Type, GuildId,
            _ => Task.FromResult(BackfillOutcome.ShortCircuit("guild missing")), default);

        var row = await ReadCheckpointAsync();
        Assert.Equal(BackfillStatus.Failed, row.Status);
        Assert.Null(row.CompletedAtUtc);
        Assert.Equal(1, row.ErrorCount);
        Assert.Contains("guild missing", row.LastError);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_MarksFailedAndSwallows()
    {
        // OperationCanceledException is the deploy-restart path: persist Failed, do not rethrow.
        await _executor.RunAsync(Type, GuildId,
            _ => throw new OperationCanceledException("restart"), default);

        var row = await ReadCheckpointAsync();
        Assert.Equal(BackfillStatus.Failed, row.Status);
        Assert.Null(row.CompletedAtUtc);
    }

    [Fact]
    public async Task RunAsync_WhenWorkThrows_MarksFailedAndRethrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _executor.RunAsync(Type, GuildId,
                _ => throw new InvalidOperationException("boom"), default));

        var row = await ReadCheckpointAsync();
        Assert.Equal(BackfillStatus.Failed, row.Status);
        Assert.Contains("boom", row.LastError);
    }

    [Fact]
    public async Task RunAsync_WhenPriorRunNotInProgress_ClearsResumeCursorAtomically()
    {
        await SeedCheckpointAsync(BackfillStatus.Completed, currentChannelId: 999UL, lastProcessedId: 888UL);

        ulong? cursorSeenInWork = 12345UL;
        await _executor.RunAsync(Type, GuildId, ctx =>
        {
            cursorSeenInWork = ctx.Checkpoint.CurrentChannelId;
            return Task.FromResult(BackfillOutcome.Completed);
        }, default);

        // Reset is visible to the work delegate (it runs against the flipped checkpoint) ...
        Assert.Null(cursorSeenInWork);
        // ... and persisted, so a crash mid-work cannot leave {InProgress, stale cursor}.
        var row = await ReadCheckpointAsync();
        Assert.Null(row.CurrentChannelId);
        Assert.Null(row.LastProcessedId);
    }

    [Fact]
    public async Task RunAsync_WhenPriorRunInterrupted_PreservesResumeCursor()
    {
        await SeedCheckpointAsync(BackfillStatus.InProgress, currentChannelId: 999UL, lastProcessedId: 888UL);

        ulong? cursorSeenInWork = null;
        await _executor.RunAsync(Type, GuildId, ctx =>
        {
            cursorSeenInWork = ctx.Checkpoint.CurrentChannelId;
            return Task.FromResult(BackfillOutcome.Completed);
        }, default);

        Assert.Equal(999UL, cursorSeenInWork);
    }

    private async Task SeedCheckpointAsync(BackfillStatus status, ulong currentChannelId, ulong lastProcessedId)
    {
        await using var db = NewContext();
        db.BackfillCheckpoints.Add(new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildId,
            Type = Type,
            Status = status,
            CurrentChannelId = currentChannelId,
            LastProcessedId = lastProcessedId,
            StartedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<BackfillCheckpointEntity> ReadCheckpointAsync()
    {
        await using var db = NewContext();
        return await db.BackfillCheckpoints.SingleAsync(c => c.GuildDiscordId == GuildId && c.Type == Type);
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
