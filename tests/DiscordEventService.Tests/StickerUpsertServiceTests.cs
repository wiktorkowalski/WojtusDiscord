using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// Contract tests for the single sticker-write seam (#290 step 3): live GuildStickersUpdated
// and the stickers backfill both go through StickerUpsertService. Pins the full column map —
// including Type/FormatType/PackId/IsAvailable, which the live handler's update branch used
// to drop — plus soft-delete revival and the nullable-guild heal/preserve semantics.
public sealed class StickerUpsertServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;
    private RecordingLogger _logger = null!;
    private StickerUpsertService _service = null!;
    private Guid _guildId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Stickers.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = 1UL, Name = "TestGuild" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _guildId = guild.Id;

        _logger = new RecordingLogger();
        _service = new StickerUpsertService(_db, _logger.For<StickerUpsertService>());
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task UpsertStickerAsync_WhenNew_InsertsFullColumnMap()
    {
        var sticker = Sticker(id: 10, name: "wojak", description: "sad wojak", tags: "sad,wojak",
            type: 2, formatType: 1, packId: 777, available: true);

        var result = await _service.UpsertStickerAsync(sticker, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Stickers.SingleAsync(s => s.DiscordId == 10UL);
        Assert.Equal(result.Value, row.Id);
        Assert.Equal(_guildId, row.GuildId);
        Assert.Equal(777UL, row.PackId);
        Assert.Equal("wojak", row.Name);
        Assert.Equal("sad wojak", row.Description);
        Assert.Equal("sad,wojak", row.Tags);
        Assert.Equal(2, row.Type);
        Assert.Equal(1, row.FormatType);
        Assert.True(row.IsAvailable);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertStickerAsync_WhenExists_UpdatesEveryMappedColumn()
    {
        _db.Stickers.Add(new StickerEntity
        {
            DiscordId = 20UL,
            GuildId = _guildId,
            Name = "before",
            Description = "old",
            Tags = "old",
            Type = 1,
            FormatType = 3,
            IsAvailable = true,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var sticker = Sticker(id: 20, name: "after", description: "new", tags: "new,tags",
            type: 2, formatType: 1, packId: 888, available: false);
        var result = await _service.UpsertStickerAsync(sticker, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Stickers.SingleAsync(s => s.DiscordId == 20UL);
        Assert.Equal("after", row.Name);
        Assert.Equal("new", row.Description);
        Assert.Equal("new,tags", row.Tags);
        Assert.Equal(2, row.Type);
        Assert.Equal(1, row.FormatType);
        Assert.Equal(888UL, row.PackId);
        Assert.False(row.IsAvailable);
    }

    [Fact]
    public async Task UpsertStickerAsync_WhenSoftDeleted_RevivesRow()
    {
        _db.Stickers.Add(new StickerEntity
        {
            DiscordId = 30UL,
            GuildId = _guildId,
            Name = "ghost",
            IsDeleted = true,
            DeletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertStickerAsync(Sticker(id: 30, name: "ghost"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Stickers.SingleAsync(s => s.DiscordId == 30UL);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertStickerAsync_WhenGuildResolvesLater_HealsNullGuildId()
    {
        _db.Stickers.Add(new StickerEntity { DiscordId = 40UL, GuildId = null, Name = "orphan" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertStickerAsync(Sticker(id: 40, name: "orphan"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Stickers.SingleAsync(s => s.DiscordId == 40UL);
        Assert.Equal(_guildId, row.GuildId);
    }

    [Fact]
    public async Task UpsertStickerAsync_WhenGuildUnresolved_PreservesExistingGuildId()
    {
        _db.Stickers.Add(new StickerEntity { DiscordId = 50UL, GuildId = _guildId, Name = "kept" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertStickerAsync(Sticker(id: 50, name: "kept"), guildId: null);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Stickers.SingleAsync(s => s.DiscordId == 50UL);
        Assert.Equal(_guildId, row.GuildId);
    }

    // DSharpPlus entities have internal setters; hydrating from gateway-shaped JSON is the
    // supported way to build one outside the library. The API sends tags as one
    // comma-separated string.
    private static DiscordMessageSticker Sticker(ulong id, string name, string? description = null,
        string? tags = null, int type = 2, int formatType = 1, ulong? packId = null, bool available = true)
    {
        var fields = new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["name"] = name,
            ["description"] = description,
            ["tags"] = tags,
            ["type"] = type,
            ["format_type"] = formatType,
            ["available"] = available,
        };
        // The gateway omits pack_id for guild stickers; a null value fails hydration.
        if (packId is not null)
            fields["pack_id"] = packId.ToString();
        var json = JsonConvert.SerializeObject(fields);
        return JsonConvert.DeserializeObject<DiscordMessageSticker>(json)
            ?? throw new InvalidOperationException("DiscordMessageSticker deserialization returned null");
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
