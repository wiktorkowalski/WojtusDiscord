using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// Contract tests for the single role-write seam (#290 step 3): every writer (live events,
// guild cold-sync, backfill) goes through RoleUpsertService, so the full column map,
// the permissions parse, and soft-delete revival are pinned here once.
public sealed class RoleUpsertServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;
    private RecordingLogger _logger = null!;
    private RoleUpsertService _service = null!;
    private Guid _guildId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Roles.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = 1UL, Name = "TestGuild" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _guildId = guild.Id;

        _logger = new RecordingLogger();
        _service = new RoleUpsertService(_db, _logger.For<RoleUpsertService>());
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task UpsertRoleAsync_WhenNew_InsertsFullColumnMap()
    {
        var role = Role(id: 10, name: "moderator", color: 0x3498DB, hoisted: true,
            position: 5, permissions: "268435456", managed: true, mentionable: true);

        var result = await _service.UpsertRoleAsync(role, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Roles.SingleAsync(r => r.DiscordId == 10UL);
        Assert.Equal(result.Value, row.Id);
        Assert.Equal(_guildId, row.GuildId);
        Assert.Equal("moderator", row.Name);
        Assert.Equal(0x3498DB, row.Color);
        Assert.True(row.IsHoisted);
        Assert.Equal(5, row.Position);
        Assert.Equal(268435456L, row.Permissions);
        Assert.True(row.IsManaged);
        Assert.True(row.IsMentionable);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertRoleAsync_WhenExists_UpdatesEveryMappedColumn()
    {
        _db.Roles.Add(new RoleEntity
        {
            DiscordId = 20UL,
            GuildId = _guildId,
            Name = "before",
            Color = 0,
            Position = 0,
            Permissions = 0,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var role = Role(id: 20, name: "after", color: 0xE74C3C, hoisted: true,
            position: 9, permissions: "8", managed: true, mentionable: true);
        var result = await _service.UpsertRoleAsync(role, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Roles.SingleAsync(r => r.DiscordId == 20UL);
        Assert.Equal("after", row.Name);
        Assert.Equal(0xE74C3C, row.Color);
        Assert.True(row.IsHoisted);
        Assert.Equal(9, row.Position);
        Assert.Equal(8L, row.Permissions);
        Assert.True(row.IsManaged);
        Assert.True(row.IsMentionable);
    }

    [Fact]
    public async Task UpsertRoleAsync_WhenSoftDeleted_RevivesRow()
    {
        _db.Roles.Add(new RoleEntity
        {
            DiscordId = 30UL,
            GuildId = _guildId,
            Name = "ghost",
            IsDeleted = true,
            DeletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertRoleAsync(Role(id: 30, name: "ghost"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Roles.SingleAsync(r => r.DiscordId == 30UL);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    // DSharpPlus entities have internal setters; hydrating from gateway-shaped JSON is the
    // supported way to build one outside the library.
    private static DiscordRole Role(ulong id, string name, int color = 0, bool hoisted = false,
        int position = 0, string permissions = "0", bool managed = false, bool mentionable = false)
    {
        var json = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["name"] = name,
            ["color"] = color,
            ["hoist"] = hoisted,
            ["position"] = position,
            ["permissions"] = permissions,
            ["managed"] = managed,
            ["mentionable"] = mentionable,
        });
        return JsonConvert.DeserializeObject<DiscordRole>(json)
            ?? throw new InvalidOperationException("DiscordRole deserialization returned null");
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
