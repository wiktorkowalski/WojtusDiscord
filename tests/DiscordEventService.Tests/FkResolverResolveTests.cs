using System.Reflection;
using DiscordEventService.Data;
using DiscordEventService.Services;
using DiscordEventService.Services.Pipeline;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace DiscordEventService.Tests;

// Instance-half coverage for FkResolver (#292): the resolver driving real upsert services
// against real Postgres. The static ValidateAsync halves are pinned in FkResolverTests.
public sealed class FkResolverResolveTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;
    private FkResolver _resolver = null!;
    private List<Exception> _recordedFailures = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.UserNameHistory.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        _db.ChangeTracker.Clear();

        _resolver = new FkResolver(
            new GuildUpsertService(_db, NullLogger<GuildUpsertService>.Instance),
            new ChannelUpsertService(_db, NullLogger<ChannelUpsertService>.Instance),
            new UserService(_db, NullLogger<UserService>.Instance));
        _recordedFailures = [];
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ResolveAsync_GuildOnly_UpsertsGuildAndReturnsItsId()
    {
        var result = await _resolver.ResolveAsync(NewEventContext(), Guild(id: 500, name: "Solo"), "GuildId=500");

        Assert.True(result.Success);
        await using var verify = NewContext();
        var row = await verify.Guilds.SingleAsync(g => g.DiscordId == 500UL);
        Assert.Equal(row.Id, result.GuildId);
        Assert.Empty(_recordedFailures);
    }

    [Fact]
    public async Task ResolveAsync_GuildChannelUser_UpsertsAllThreeAndReturnsTheirIds()
    {
        var result = await _resolver.ResolveAsync(
            NewEventContext(),
            Guild(id: 600, name: "Trio"),
            Channel(id: 601, type: 0, name: "general"),
            User(id: 602, username: "resolver-user"),
            "MessageId=603");

        Assert.True(result.Success);
        await using var verify = NewContext();
        var guildRow = await verify.Guilds.SingleAsync(g => g.DiscordId == 600UL);
        var channelRow = await verify.Channels.SingleAsync(c => c.DiscordId == 601UL);
        var userRow = await verify.Users.SingleAsync(u => u.DiscordId == 602UL);
        Assert.Equal(guildRow.Id, result.GuildId);
        Assert.Equal(channelRow.Id, result.ChannelId);
        Assert.Equal(userRow.Id, result.UserId);
        Assert.Equal(guildRow.Id, channelRow.GuildId);
        Assert.Empty(_recordedFailures);
    }

    [Fact]
    public async Task ResolveAsync_GuildChannel_UpsertsBothAndReturnsTheirIds()
    {
        var result = await _resolver.ResolveAsync(
            NewEventContext(),
            Guild(id: 800, name: "Pair"),
            Channel(id: 801, type: 0, name: "invites"),
            "InviteCode=abc");

        Assert.True(result.Success);
        await using var verify = NewContext();
        var guildRow = await verify.Guilds.SingleAsync(g => g.DiscordId == 800UL);
        var channelRow = await verify.Channels.SingleAsync(c => c.DiscordId == 801UL);
        Assert.Equal(guildRow.Id, result.GuildId);
        Assert.Equal(channelRow.Id, result.ChannelId);
        Assert.Equal(guildRow.Id, channelRow.GuildId);
        Assert.Empty(_recordedFailures);
    }

    [Fact]
    public async Task ResolveAsync_GuildUser_UpsertsBothAndReturnsTheirIds()
    {
        var result = await _resolver.ResolveAsync(
            NewEventContext(),
            Guild(id: 700, name: "Duo"),
            User(id: 701, username: "duo-user"),
            "GuildId=700 UserId=701");

        Assert.True(result.Success);
        await using var verify = NewContext();
        var guildRow = await verify.Guilds.SingleAsync(g => g.DiscordId == 700UL);
        var userRow = await verify.Users.SingleAsync(u => u.DiscordId == 701UL);
        Assert.Equal(guildRow.Id, result.GuildId);
        Assert.Equal(userRow.Id, result.UserId);
        Assert.Empty(_recordedFailures);
    }

    private EventContext NewEventContext() =>
        new EventContext(
            Db: _db,
            Services: null!,
            CorrelationId: Guid.NewGuid(),
            RawJson: null,
            ReceivedAtUtc: DateTime.UnixEpoch,
            Logger: NullLogger.Instance,
            RecordFailureAsync: ex =>
            {
                _recordedFailures.Add(ex);
                return Task.CompletedTask;
            });

    // DSharpPlus entities have internal setters; hydrating from gateway-shaped JSON is the
    // supported way to build one outside the library.
    private static DiscordGuild Guild(ulong id, string name)
    {
        var json = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["name"] = name,
        });
        return JsonConvert.DeserializeObject<DiscordGuild>(json)
            ?? throw new InvalidOperationException("DiscordGuild deserialization returned null");
    }

    private static DiscordChannel Channel(ulong id, int type, string name)
    {
        var json = JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["type"] = type,
            ["name"] = name,
        });
        return JsonConvert.DeserializeObject<DiscordChannel>(json)
            ?? throw new InvalidOperationException("DiscordChannel deserialization returned null");
    }

    // DiscordUser exposes a parameterless ctor with non-public setters; reflection is the only
    // way to build one outside the library.
    private static DiscordUser User(ulong id, string username)
    {
        var user = (DiscordUser)Activator.CreateInstance(typeof(DiscordUser), nonPublic: true)!;
        SetProp(user, nameof(DiscordUser.Id), id);
        SetProp(user, nameof(DiscordUser.Username), username);
        SetProp(user, nameof(DiscordUser.Discriminator), "0");
        return user;
    }

    private static void SetProp(DiscordUser user, string name, object value)
    {
        var property = typeof(DiscordUser).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        property!.SetValue(user, value);
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
