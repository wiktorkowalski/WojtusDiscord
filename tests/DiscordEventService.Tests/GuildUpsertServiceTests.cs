using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// Contract tests for the single guild-write seam: every writer (GuildCreated/GuildAvailable,
// boot cold-sync, FkResolver, sticker/emoji/automod handlers) goes through GuildUpsertService,
// so the full column map — including clearing a stale LeftAtUtc marker — is pinned here once.
public sealed class GuildUpsertServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;
    private RecordingLogger _logger = null!;
    private GuildUpsertService _service = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        _logger = new RecordingLogger();
        _service = new GuildUpsertService(_db, _logger.For<GuildUpsertService>());
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task UpsertGuildAsync_WhenNew_InsertsFullColumnMap()
    {
        var guild = Guild(id: 10, name: "Alpha", iconHash: "abc123", ownerId: 42);

        var result = await _service.UpsertGuildAsync(guild);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Guilds.SingleAsync(g => g.DiscordId == 10UL);
        Assert.Equal(result.Value, row.Id);
        Assert.Equal("Alpha", row.Name);
        Assert.Equal("abc123", row.IconHash);
        Assert.Equal(42UL, row.OwnerId);
        Assert.Null(row.LeftAtUtc);
    }

    [Fact]
    public async Task UpsertGuildAsync_WhenExists_UpdatesEveryMappedColumnAndClearsLeftAtUtc()
    {
        _db.Guilds.Add(new GuildEntity
        {
            DiscordId = 20UL,
            Name = "Before",
            IconHash = "old",
            OwnerId = 1UL,
            LeftAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();
        var originalId = await _db.Guilds.Where(g => g.DiscordId == 20UL).Select(g => g.Id).SingleAsync();
        _db.ChangeTracker.Clear();

        var guild = Guild(id: 20, name: "After", iconHash: "new", ownerId: 2);

        var result = await _service.UpsertGuildAsync(guild);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalId, result.Value);
        await using var verify = NewContext();
        var row = await verify.Guilds.SingleAsync(g => g.DiscordId == 20UL);
        Assert.Equal("After", row.Name);
        Assert.Equal("new", row.IconHash);
        Assert.Equal(2UL, row.OwnerId);
        // Upserting means the bot just observed the guild live — a stale "left" marker must clear.
        Assert.Null(row.LeftAtUtc);
    }

    private static DiscordGuild Guild(ulong id, string name, string? iconHash = null, ulong ownerId = 0)
    {
        var json = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["name"] = name,
            ["icon"] = iconHash,
            ["owner_id"] = ownerId.ToString(),
        });
        return JsonConvert.DeserializeObject<DiscordGuild>(json)
            ?? throw new InvalidOperationException("DiscordGuild deserialization returned null");
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
