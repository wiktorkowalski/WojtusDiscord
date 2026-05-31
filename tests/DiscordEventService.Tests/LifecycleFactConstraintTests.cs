using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class LifecycleFactConstraintTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Bans.ExecuteDeleteAsync();
        await _db.Activities.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Activity_ActiveWithEndTimestamp_ViolatesLifecycleCheck()
    {
        var userId = await SeedUserAsync(10UL);
        _db.Activities.Add(new ActivityEntity { UserId = userId, ActivityType = 0, Name = "X", IsActive = true, EndedAtUtc = DateTime.UtcNow });

        await AssertCheckViolationAsync("ck_activities_lifecycle");
    }

    [Fact]
    public async Task Activity_InactiveWithoutEndTimestamp_ViolatesLifecycleCheck()
    {
        var userId = await SeedUserAsync(11UL);
        _db.Activities.Add(new ActivityEntity { UserId = userId, ActivityType = 0, Name = "X", IsActive = false, EndedAtUtc = null });

        await AssertCheckViolationAsync("ck_activities_lifecycle");
    }

    [Fact]
    public async Task Activity_ConsistentStates_Persist()
    {
        var userId = await SeedUserAsync(12UL);
        _db.Activities.Add(new ActivityEntity { UserId = userId, ActivityType = 0, Name = "Active", IsActive = true, EndedAtUtc = null });
        _db.Activities.Add(new ActivityEntity { UserId = userId, ActivityType = 0, Name = "Ended", IsActive = false, EndedAtUtc = DateTime.UtcNow });

        await _db.SaveChangesAsync();

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Activities.CountAsync());
    }

    [Fact]
    public async Task Ban_InactiveWithoutUnbanTimestamp_ViolatesLifecycleCheck()
    {
        var (guildId, userId) = await SeedGuildAndUserAsync(20UL);
        _db.Bans.Add(new BanEntity { GuildId = guildId, UserId = userId, BannedAtUtc = DateTime.UtcNow, IsActive = false, UnbannedAtUtc = null });

        await AssertCheckViolationAsync("ck_bans_lifecycle");
    }

    [Fact]
    public async Task Ban_ConsistentStates_Persist()
    {
        var (guildId, userId) = await SeedGuildAndUserAsync(21UL);
        _db.Bans.Add(new BanEntity { GuildId = guildId, UserId = userId, BannedAtUtc = DateTime.UtcNow, IsActive = true, UnbannedAtUtc = null });

        await _db.SaveChangesAsync();

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Bans.CountAsync(b => b.IsActive));
    }

    private async Task AssertCheckViolationAsync(string constraintName)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23514", pg.SqlState); // check_violation
        Assert.Contains(constraintName, pg.ConstraintName);
    }

    private async Task<Guid> SeedUserAsync(ulong discordId)
    {
        var user = new UserEntity { DiscordId = discordId, Username = $"u{discordId}" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return user.Id;
    }

    private async Task<(Guid GuildId, Guid UserId)> SeedGuildAndUserAsync(ulong discordId)
    {
        var guild = new GuildEntity { DiscordId = discordId, Name = $"g{discordId}" };
        var user = new UserEntity { DiscordId = discordId, Username = $"u{discordId}" };
        _db.Guilds.Add(guild);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (guild.Id, user.Id);
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
