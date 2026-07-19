using System.Reflection;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class UserUpsertTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.UserNameHistory.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task UpsertUserAsync_WhenNew_InsertsUserAndWritesNoHistory()
    {
        var service = new UserService(_db, NullLogger<UserService>.Instance);

        await service.UpsertUserAsync(MakeUser(100UL, "alice", "Alice"));

        await using var verify = NewContext();
        var row = await verify.Users.SingleAsync(u => u.DiscordId == 100UL);
        Assert.Equal("alice", row.Username);
        Assert.Equal("Alice", row.GlobalName);
        Assert.Equal(0, await verify.UserNameHistory.CountAsync());
    }

    [Fact]
    public async Task UpsertUserAsync_WhenNameChanged_UpdatesUserAndWritesHistory()
    {
        var service = new UserService(_db, NullLogger<UserService>.Instance);
        await service.UpsertUserAsync(MakeUser(200UL, "bob", "Bob"));
        _db.ChangeTracker.Clear();

        await service.UpsertUserAsync(MakeUser(200UL, "bobby", "Bobby"));

        await using var verify = NewContext();
        var row = await verify.Users.SingleAsync(u => u.DiscordId == 200UL);
        Assert.Equal("bobby", row.Username);
        Assert.Equal("Bobby", row.GlobalName);

        var history = await verify.UserNameHistory.SingleAsync(h => h.UserId == row.Id);
        Assert.Equal("bob", history.UsernameBefore);
        Assert.Equal("bobby", history.UsernameAfter);
        Assert.Equal("Bob", history.GlobalNameBefore);
        Assert.Equal("Bobby", history.GlobalNameAfter);
    }

    [Fact]
    public async Task UpsertUserAsync_WhenUnchanged_WritesNoHistory()
    {
        var service = new UserService(_db, NullLogger<UserService>.Instance);
        await service.UpsertUserAsync(MakeUser(300UL, "carol", "Carol"));
        _db.ChangeTracker.Clear();

        await service.UpsertUserAsync(MakeUser(300UL, "carol", "Carol"));

        await using var verify = NewContext();
        Assert.Equal(0, await verify.UserNameHistory.CountAsync());
    }

    [Fact]
    public async Task UpsertUserAsync_WhenOnlyAvatarChanged_UpdatesUserWithoutHistory()
    {
        var service = new UserService(_db, NullLogger<UserService>.Instance);
        await service.UpsertUserAsync(MakeUser(500UL, "dave", "Dave", avatarHash: "avatar_v1"));
        _db.ChangeTracker.Clear();

        // Same name, different avatar: the common set-based path must still refresh mutable fields
        // but must NOT write a name-history row (history is for username/globalName changes only).
        await service.UpsertUserAsync(MakeUser(500UL, "dave", "Dave", avatarHash: "avatar_v2"));

        await using var verify = NewContext();
        var row = await verify.Users.SingleAsync(u => u.DiscordId == 500UL);
        Assert.Equal("avatar_v2", row.AvatarHash);
        Assert.Equal("dave", row.Username);
        Assert.Equal(0, await verify.UserNameHistory.CountAsync());
    }

    [Fact]
    public async Task UpsertUserAsync_WhenUsernameCaseOnlyChanged_PersistsCasingWithoutHistory()
    {
        var service = new UserService(_db, NullLogger<UserService>.Instance);
        await service.UpsertUserAsync(MakeUser(600UL, "clyde", "Clyde"));
        _db.ChangeTracker.Clear();

        // System users case-flap between API surfaces (#297): the new casing must be persisted,
        // but a case-only username difference must not produce a name-history row.
        await service.UpsertUserAsync(MakeUser(600UL, "Clyde", "Clyde"));

        await using var verify = NewContext();
        var row = await verify.Users.SingleAsync(u => u.DiscordId == 600UL);
        Assert.Equal("Clyde", row.Username);
        Assert.Equal(0, await verify.UserNameHistory.CountAsync());
    }

    [Fact]
    public async Task UpsertUserAsync_WhenUsernameCaseFlappedAndGlobalNameChanged_RecordsOnlyGlobalName()
    {
        var service = new UserService(_db, NullLogger<UserService>.Instance);
        await service.UpsertUserAsync(MakeUser(700UL, "clyde", "Clyde"));
        _db.ChangeTracker.Clear();

        await service.UpsertUserAsync(MakeUser(700UL, "Clyde", "Clyde AI"));

        await using var verify = NewContext();
        var row = await verify.Users.SingleAsync(u => u.DiscordId == 700UL);
        Assert.Equal("Clyde", row.Username);
        Assert.Equal("Clyde AI", row.GlobalName);

        var history = await verify.UserNameHistory.SingleAsync(h => h.UserId == row.Id);
        Assert.Null(history.UsernameBefore);
        Assert.Null(history.UsernameAfter);
        Assert.Equal("Clyde", history.GlobalNameBefore);
        Assert.Equal("Clyde AI", history.GlobalNameAfter);
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    // DiscordUser exposes a parameterless ctor with non-public setters; reflection is the only
    // way to build a fixture instance without a live gateway connection.
    private static DiscordUser MakeUser(ulong id, string username, string globalName, string? avatarHash = null)
    {
        var user = (DiscordUser)Activator.CreateInstance(typeof(DiscordUser), nonPublic: true)!;
        SetProp(user, nameof(DiscordUser.Id), id);
        SetProp(user, nameof(DiscordUser.Username), username);
        SetProp(user, nameof(DiscordUser.GlobalName), globalName);
        SetProp(user, nameof(DiscordUser.Discriminator), "0");
        if (avatarHash is not null)
            SetProp(user, nameof(DiscordUser.AvatarHash), avatarHash);
        return user;
    }

    private static void SetProp(DiscordUser user, string name, object value)
    {
        var property = typeof(DiscordUser).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        property!.SetValue(user, value);
    }
}
