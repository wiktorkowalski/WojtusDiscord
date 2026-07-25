using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DiscordEventService.Tests;

// Pins the #314 contract: a 23505 the upsert primitives handle by design must leave no Error
// record behind, while staying visible at Warning — above the prod category floor, so the SQL
// and parameters EF renders on a real failure survive too.
public sealed class UpsertConflictLogLevelTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly RecordingLogger _log = new();

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await db.Guilds.ExecuteDeleteAsync();
        _log.Entries.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpsertAsync_WhenInsertConflicts_LogsNoErrorAndKeepsWarningDiagnostic()
    {
        await SeedAsync(500UL);

        await using var db = NewContext();
        // The match never hits the seeded row, so ExecuteUpdate affects 0 rows and the insert
        // path runs; the factory inserts the seeded DiscordId, colliding on the unique index.
        var id = await db.Guilds.UpsertAsync(
            g => g.DiscordId == 501UL,
            s => s.SetProperty(g => g.Name, "Retry"),
            () => new GuildEntity { DiscordId = 500UL, Name = "Conflict" },
            g => g.Id);

        Assert.Equal(Guid.Empty, id);
        AssertConflictWasHandledQuietly();
    }

    [Fact]
    public async Task GetOrInsertAsync_WhenInsertConflicts_LogsNoErrorAndKeepsWarningDiagnostic()
    {
        await SeedAsync(600UL);

        await using var db = NewContext();
        var (entity, inserted) = await db.Guilds.GetOrInsertAsync(
            g => g.DiscordId == 600UL,
            () => new GuildEntity { DiscordId = 600UL, Name = "Conflict" });

        Assert.False(inserted);
        Assert.Equal("Existing", entity?.Name);
        AssertConflictWasHandledQuietly();
    }

    private void AssertConflictWasHandledQuietly()
    {
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Error);

        // Downgraded, not silenced — Warning clears the appsettings.json floor for EF, so this
        // assertion describes prod behaviour and not just the always-enabled test logger.
        Assert.Contains(_log.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("23505", StringComparison.Ordinal));
    }

    private async Task SeedAsync(ulong discordId)
    {
        await using var db = NewContext();
        db.Guilds.Add(new GuildEntity { DiscordId = discordId, Name = "Existing" });
        await db.SaveChangesAsync();
        _log.Entries.Clear();
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .UseLoggerFactory(_log.AsLoggerFactory())
            .Options;
        return new DiscordDbContext(options);
    }
}
