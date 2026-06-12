using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class BackfillIterateWithCheckpointTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildId = 556_000_001UL;
    private static readonly ulong[] Items = [10UL, 20UL, 30UL, 40UL];

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.BackfillCheckpoints.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task IterateWithCheckpointAsync_FreshRun_VisitsAllItemsInOrderWithNullCursor()
    {
        var checkpoint = await SeedAsync(currentChannelId: null, lastProcessedId: null);

        var observed = await RunAsync(checkpoint, Items);

        Assert.Equal([(10UL, null), (20UL, null), (30UL, null), (40UL, null)], observed);
        Assert.Equal(40UL, checkpoint.CurrentChannelId);
        Assert.Null(checkpoint.LastProcessedId);
    }

    [Fact]
    public async Task IterateWithCheckpointAsync_ResumeMidList_SkipsToCursorAndKeepsSavedBatchCursor()
    {
        // The discriminating assertion: item 30 must observe LastProcessedId=777 on entry (its saved
        // batch cursor) and 40 must observe null. Final state is identical whether or not the reset
        // is correctly ordered, so only the per-item observation catches the off-by-one.
        var checkpoint = await SeedAsync(currentChannelId: 30UL, lastProcessedId: 777UL);

        var observed = await RunAsync(checkpoint, Items);

        Assert.Equal([(30UL, (ulong?)777UL), (40UL, null)], observed);
        Assert.Equal(40UL, checkpoint.CurrentChannelId);
    }

    [Fact]
    public async Task IterateWithCheckpointAsync_ResumeCursorNotInList_RestartsAndDiscardsBatchCursor()
    {
        var checkpoint = await SeedAsync(currentChannelId: 999UL, lastProcessedId: 777UL);

        var observed = await RunAsync(checkpoint, Items);

        Assert.Equal([(10UL, null), (20UL, null), (30UL, null), (40UL, null)], observed);
    }

    [Fact]
    public async Task IterateWithCheckpointAsync_PerItemFailure_RecordsErrorAndContinues()
    {
        var checkpoint = await SeedAsync(currentChannelId: null, lastProcessedId: null);
        var visited = new List<ulong>();

        var job = new HarnessJob();
        await job.IterateAsync(_db, checkpoint, [10UL, 20UL, 30UL], x => x, item =>
        {
            visited.Add(item);
            return item == 20UL ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }, default);

        Assert.Equal([10UL, 20UL, 30UL], visited);
        Assert.Equal(1, checkpoint.ErrorCount);
        Assert.Contains("boom", checkpoint.LastError);
        Assert.Equal(30UL, checkpoint.CurrentChannelId);
    }

    private async Task<List<(ulong item, ulong? cursorAtEntry)>> RunAsync(
        BackfillCheckpointEntity checkpoint, IReadOnlyList<ulong> items)
    {
        var observed = new List<(ulong, ulong?)>();
        var job = new HarnessJob();
        await job.IterateAsync(_db, checkpoint, items, x => x, item =>
        {
            observed.Add((item, checkpoint.LastProcessedId));
            return Task.CompletedTask;
        }, default);
        return observed;
    }

    private async Task<BackfillCheckpointEntity> SeedAsync(ulong? currentChannelId, ulong? lastProcessedId)
    {
        var checkpoint = new BackfillCheckpointEntity
        {
            GuildDiscordId = GuildId,
            Type = BackfillType.Messages,
            Status = BackfillStatus.InProgress,
            CurrentChannelId = currentChannelId,
            LastProcessedId = lastProcessedId,
            StartedAtUtc = DateTime.UtcNow
        };
        _db.BackfillCheckpoints.Add(checkpoint);
        await _db.SaveChangesAsync();
        return checkpoint;
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    // Minimal concrete subclass to exercise the protected IterateWithCheckpointAsync helper.
    private sealed class HarnessJob : BackfillJobBase
    {
        protected override BackfillType BackfillType => BackfillType.Messages;

        public Task IterateAsync<T>(
            DiscordDbContext db,
            BackfillCheckpointEntity checkpoint,
            IReadOnlyList<T> items,
            Func<T, ulong> keyOf,
            Func<T, Task> processItem,
            CancellationToken cancellationToken)
            => IterateWithCheckpointAsync(db, checkpoint, items, keyOf, processItem, NullLogger.Instance, cancellationToken);
    }
}
