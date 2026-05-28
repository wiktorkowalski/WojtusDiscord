using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class GetOrInsertTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Guilds.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task GetOrInsertAsync_WhenNew_InsertsAndReportsInserted()
    {
        var (entity, inserted) = await _db.Guilds.GetOrInsertAsync(
            g => g.DiscordId == 100UL,
            () => new GuildEntity { DiscordId = 100UL, Name = "Alpha" });

        Assert.True(inserted);
        Assert.NotNull(entity);
        Assert.NotEqual(Guid.Empty, entity!.Id);

        await using var verify = NewContext();
        var row = await verify.Guilds.SingleAsync(g => g.DiscordId == 100UL);
        Assert.Equal("Alpha", row.Name);
    }

    [Fact]
    public async Task GetOrInsertAsync_WhenExists_ReturnsExistingRowUntouched()
    {
        _db.Guilds.Add(new GuildEntity { DiscordId = 200UL, Name = "Original" });
        await _db.SaveChangesAsync();
        var originalId = await _db.Guilds.Where(g => g.DiscordId == 200UL).Select(g => g.Id).SingleAsync();
        _db.ChangeTracker.Clear();

        // The factory collides on the unique DiscordId index → 23505 → existing row is returned
        // WITHOUT being overwritten (the data-loss guard the placeholder/snapshot paths rely on).
        var (entity, inserted) = await _db.Guilds.GetOrInsertAsync(
            g => g.DiscordId == 200UL,
            () => new GuildEntity { DiscordId = 200UL, Name = "SHOULD_NOT_OVERWRITE" });

        Assert.False(inserted);
        Assert.NotNull(entity);
        Assert.Equal(originalId, entity!.Id);
        Assert.Equal("Original", entity.Name);

        await using var verify = NewContext();
        var rows = await verify.Guilds.Where(g => g.DiscordId == 200UL).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Original", rows[0].Name);
    }

    [Fact]
    public async Task GetOrInsertAsync_WhenConcurrent_CreatesSingleRowAndOneInserter()
    {
        const ulong discordId = 400UL;
        var tasks = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = NewContext();
            return await ctx.Guilds.GetOrInsertAsync(
                g => g.DiscordId == discordId,
                () => new GuildEntity { DiscordId = discordId, Name = $"Concurrent-{i}" });
        });

        var results = await Task.WhenAll(tasks);

        // Exactly one caller wins the insert; the rest observe the conflict and return the row.
        Assert.Equal(1, results.Count(r => r.Inserted));
        Assert.All(results, r => Assert.NotNull(r.Entity));
        var ids = results.Select(r => r.Entity!.Id).Distinct().ToList();
        Assert.Single(ids);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Guilds.CountAsync(g => g.DiscordId == discordId));
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
