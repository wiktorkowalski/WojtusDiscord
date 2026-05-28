using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    // uuidv7() default value SQL in the migrations requires PostgreSQL 18.
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

public sealed class GuildUpsertTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
    public async Task UpsertAsync_WhenNew_InsertsRowAndReturnsId()
    {
        var id = await _db.Guilds.UpsertAsync(
            g => g.DiscordId == 100UL,
            s => s.SetProperty(g => g.Name, "Alpha"),
            () => new GuildEntity { DiscordId = 100UL, Name = "Alpha" },
            g => g.Id);

        Assert.NotEqual(Guid.Empty, id);
        await using var verify = NewContext();
        var row = await verify.Guilds.SingleAsync(g => g.DiscordId == 100UL);
        Assert.Equal("Alpha", row.Name);
    }

    [Fact]
    public async Task UpsertAsync_WhenExists_UpdatesRowKeepingId()
    {
        _db.Guilds.Add(new GuildEntity { DiscordId = 200UL, Name = "Before" });
        await _db.SaveChangesAsync();
        var originalId = await _db.Guilds.Where(g => g.DiscordId == 200UL).Select(g => g.Id).SingleAsync();
        _db.ChangeTracker.Clear();

        var id = await _db.Guilds.UpsertAsync(
            g => g.DiscordId == 200UL,
            s => s.SetProperty(g => g.Name, "After"),
            () => new GuildEntity { DiscordId = 200UL, Name = "SHOULD_NOT_BE_USED" },
            g => g.Id);

        Assert.Equal(originalId, id);
        await using var verify = NewContext();
        var rows = await verify.Guilds.Where(g => g.DiscordId == 200UL).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("After", rows[0].Name);
    }

    [Fact]
    public async Task UpsertAsync_WhenInsertConflicts_SwallowsViolationAndReturnsDefault()
    {
        _db.Guilds.Add(new GuildEntity { DiscordId = 300UL, Name = "Existing" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // match never hits the existing row, so ExecuteUpdate affects 0 rows and the insert
        // path runs; the factory inserts DiscordId=300, which collides on the unique index → 23505.
        var id = await _db.Guilds.UpsertAsync(
            g => g.DiscordId == 999UL,
            s => s.SetProperty(g => g.Name, "Retry"),
            () => new GuildEntity { DiscordId = 300UL, Name = "Conflict" },
            g => g.Id);

        Assert.Equal(Guid.Empty, id);
        await using var verify = NewContext();
        var rows = await verify.Guilds.ToListAsync();
        Assert.Single(rows);
        Assert.Equal(300UL, rows[0].DiscordId);
        Assert.Equal("Existing", rows[0].Name);
    }

    [Fact]
    public async Task UpsertAsync_WhenConcurrent_CreatesSingleRow()
    {
        const ulong discordId = 400UL;
        var tasks = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = NewContext();
            return await ctx.Guilds.UpsertAsync(
                g => g.DiscordId == discordId,
                s => s.SetProperty(g => g.Name, $"Concurrent-{i}"),
                () => new GuildEntity { DiscordId = discordId, Name = $"Concurrent-{i}" },
                g => g.Id);
        });

        var ids = await Task.WhenAll(tasks);

        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Single(ids.Distinct());
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
