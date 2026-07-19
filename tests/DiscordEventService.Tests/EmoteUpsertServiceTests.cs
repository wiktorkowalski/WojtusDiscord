using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// Contract tests for the single emote-write seam (#290 step 3): live GuildEmojisUpdated and
// the emojis backfill both go through EmoteUpsertService, so the column map, soft-delete
// revival, and the nullable-guild heal/preserve semantics are pinned here once.
public sealed class EmoteUpsertServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;
    private RecordingLogger _logger = null!;
    private EmoteUpsertService _service = null!;
    private Guid _guildId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Emotes.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = 1UL, Name = "TestGuild" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _guildId = guild.Id;

        _logger = new RecordingLogger();
        _service = new EmoteUpsertService(_db, _logger.For<EmoteUpsertService>());
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task UpsertEmoteAsync_WhenNew_InsertsFullColumnMap()
    {
        var emoji = Emoji(id: 10, name: "pepe", animated: true, available: true);

        var result = await _service.UpsertEmoteAsync(emoji, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Emotes.SingleAsync(e => e.DiscordId == 10UL);
        Assert.Equal(result.Value, row.Id);
        Assert.Equal(_guildId, row.GuildId);
        Assert.Equal("pepe", row.Name);
        Assert.True(row.IsAnimated);
        Assert.True(row.IsAvailable);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertEmoteAsync_WhenExists_UpdatesEveryMappedColumn()
    {
        _db.Emotes.Add(new EmoteEntity
        {
            DiscordId = 20UL,
            GuildId = _guildId,
            Name = "before",
            IsAnimated = false,
            IsAvailable = false,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertEmoteAsync(
            Emoji(id: 20, name: "after", animated: true, available: true), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Emotes.SingleAsync(e => e.DiscordId == 20UL);
        Assert.Equal("after", row.Name);
        Assert.True(row.IsAnimated);
        Assert.True(row.IsAvailable);
    }

    [Fact]
    public async Task UpsertEmoteAsync_WhenSoftDeleted_RevivesRow()
    {
        _db.Emotes.Add(new EmoteEntity
        {
            DiscordId = 30UL,
            GuildId = _guildId,
            Name = "ghost",
            IsDeleted = true,
            DeletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertEmoteAsync(Emoji(id: 30, name: "ghost"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Emotes.SingleAsync(e => e.DiscordId == 30UL);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertEmoteAsync_WhenGuildResolvesLater_HealsNullGuildId()
    {
        _db.Emotes.Add(new EmoteEntity { DiscordId = 40UL, GuildId = null, Name = "orphan" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertEmoteAsync(Emoji(id: 40, name: "orphan"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Emotes.SingleAsync(e => e.DiscordId == 40UL);
        Assert.Equal(_guildId, row.GuildId);
    }

    [Fact]
    public async Task UpsertEmoteAsync_WhenGuildUnresolved_PreservesExistingGuildId()
    {
        _db.Emotes.Add(new EmoteEntity { DiscordId = 50UL, GuildId = _guildId, Name = "kept" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertEmoteAsync(Emoji(id: 50, name: "kept"), guildId: null);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Emotes.SingleAsync(e => e.DiscordId == 50UL);
        Assert.Equal(_guildId, row.GuildId);
    }

    // DSharpPlus entities have internal setters; hydrating from gateway-shaped JSON is the
    // supported way to build one outside the library.
    private static DiscordEmoji Emoji(ulong id, string name, bool animated = false, bool available = true)
    {
        var json = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["name"] = name,
            ["animated"] = animated,
            ["available"] = available,
        });
        return JsonConvert.DeserializeObject<DiscordEmoji>(json)
            ?? throw new InvalidOperationException("DiscordEmoji deserialization returned null");
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
