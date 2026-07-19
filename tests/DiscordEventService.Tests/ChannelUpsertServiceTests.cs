using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// Contract tests for the single channel-write seam (#290): every writer (live events,
// guild cold-sync, backfill) goes through ChannelUpsertService, so the full column map,
// soft-delete revival, and unknown-type handling are pinned here once.
public sealed class ChannelUpsertServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;
    private RecordingLogger _logger = null!;
    private ChannelUpsertService _service = null!;
    private Guid _guildId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = 1UL, Name = "TestGuild" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _guildId = guild.Id;

        _logger = new RecordingLogger();
        _service = new ChannelUpsertService(_db, _logger.For<ChannelUpsertService>());
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task UpsertChannelAsync_WhenNew_InsertsFullColumnMap()
    {
        var channel = Channel(id: 10, type: 2, name: "voice-lounge", topic: "hangout",
            position: 3, parentId: 99, bitrate: 64000, userLimit: 5, rateLimit: 30, nsfw: true);

        var result = await _service.UpsertChannelAsync(channel, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Channels.SingleAsync(c => c.DiscordId == 10UL);
        Assert.Equal(result.Value, row.Id);
        Assert.Equal(_guildId, row.GuildId);
        Assert.Equal("voice-lounge", row.Name);
        Assert.Equal(ChannelType.Voice, row.Type);
        Assert.Equal("hangout", row.Topic);
        Assert.Equal(3, row.Position);
        Assert.Equal(99UL, row.ParentDiscordId);
        Assert.Equal(64000, row.Bitrate);
        Assert.Equal(5, row.UserLimit);
        Assert.Equal(30, row.RateLimitPerUser);
        Assert.True(row.IsNsfw);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertChannelAsync_WhenExists_UpdatesEveryMappedColumn()
    {
        _db.Channels.Add(new ChannelEntity
        {
            DiscordId = 20UL,
            GuildId = _guildId,
            Name = "before",
            Type = ChannelType.Text,
            Position = 0,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var channel = Channel(id: 20, type: 0, name: "after", topic: "new-topic",
            position: 7, parentId: 42, bitrate: null, userLimit: null, rateLimit: 60, nsfw: true);
        var result = await _service.UpsertChannelAsync(channel, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Channels.SingleAsync(c => c.DiscordId == 20UL);
        Assert.Equal("after", row.Name);
        Assert.Equal("new-topic", row.Topic);
        Assert.Equal(7, row.Position);
        Assert.Equal(42UL, row.ParentDiscordId);
        Assert.Equal(60, row.RateLimitPerUser);
        Assert.True(row.IsNsfw);
    }

    [Fact]
    public async Task UpsertChannelAsync_WhenSoftDeleted_RevivesRow()
    {
        _db.Channels.Add(new ChannelEntity
        {
            DiscordId = 30UL,
            GuildId = _guildId,
            Name = "ghost",
            Type = ChannelType.Text,
            IsDeleted = true,
            DeletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.UpsertChannelAsync(Channel(id: 30, type: 0, name: "ghost"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Channels.SingleAsync(c => c.DiscordId == 30UL);
        Assert.False(row.IsDeleted);
        Assert.Null(row.DeletedAtUtc);
    }

    [Fact]
    public async Task UpsertChannelAsync_WhenUnknownType_PreservesRawIntAndWarns()
    {
        var result = await _service.UpsertChannelAsync(Channel(id: 40, type: 99, name: "future-type"), _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Channels.SingleAsync(c => c.DiscordId == 40UL);
        // Data contract: the raw Discord type int is persisted even when unmodeled —
        // Unknown(-1) would destroy information. Drift stays visible via the warning.
        Assert.Equal((ChannelType)99, row.Type);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task UpsertChannelAsync_ForThread_StoresParentDiscordId()
    {
        var thread = Channel(id: 50, type: 11, name: "some-thread", parentId: 10);

        var result = await _service.UpsertChannelAsync(thread, _guildId);

        Assert.True(result.IsSuccess);
        await using var verify = NewContext();
        var row = await verify.Channels.SingleAsync(c => c.DiscordId == 50UL);
        Assert.Equal(ChannelType.PublicThread, row.Type);
        Assert.Equal(10UL, row.ParentDiscordId);
    }

    // DSharpPlus entities have internal setters; hydrating from gateway-shaped JSON is the
    // supported way to build one outside the library.
    private static DiscordChannel Channel(ulong id, int type, string name, string? topic = null,
        int position = 0, ulong? parentId = null, int? bitrate = null, int? userLimit = null,
        int? rateLimit = null, bool nsfw = false)
    {
        var json = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["type"] = type,
            ["name"] = name,
            ["topic"] = topic,
            ["position"] = position,
            ["parent_id"] = parentId?.ToString(),
            ["bitrate"] = bitrate,
            ["user_limit"] = userLimit,
            ["rate_limit_per_user"] = rateLimit,
            ["nsfw"] = nsfw,
        });
        return JsonConvert.DeserializeObject<DiscordChannel>(json)
            ?? throw new InvalidOperationException("DiscordChannel deserialization returned null");
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
