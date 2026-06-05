using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class GuildControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildSf = 742554855180206203UL;
    private const ulong Alice = 100UL;   // online (desktop=3)
    private const ulong Bob = 200UL;     // idle (mobile=1)
    private const ulong Carol = 300UL;   // offline -> excluded
    private const ulong BotUser = 400UL; // online but bot -> excluded

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.PresenceEvents.ExecuteDeleteAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        await _db.Members.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Get_ReturnsCountsAndOnlineDerivation()
    {
        var controller = new GuildController(_db);

        var result = (await controller.Get(default)).Value!;

        Assert.Equal(GuildSf, result.DiscordId);
        Assert.Equal(1, result.MemberCount);   // only Alice has a member row
        Assert.Equal(1, result.ChannelCount);  // one non-deleted channel
        Assert.Equal(4, result.UserCount);
        Assert.NotNull(result.EventSpanStartUtc);

        // Carol (offline) and the bot are excluded; Alice + Bob remain.
        Assert.Equal(2, result.Online.Count);
        var alice = result.Online.Single(o => o.UserDiscordId == Alice);
        Assert.Equal("online", alice.Status);
        var bob = result.Online.Single(o => o.UserDiscordId == Bob);
        Assert.Equal("idle", bob.Status);
        Assert.DoesNotContain(result.Online, o => o.UserDiscordId == Carol);
        Assert.DoesNotContain(result.Online, o => o.UserDiscordId == BotUser);
    }

    private async Task SeedAsync()
    {
        var guild = new GuildEntity { DiscordId = GuildSf, Name = "Guild", IconHash = "icon123" };
        var alice = new UserEntity { DiscordId = Alice, Username = "alice" };
        var bob = new UserEntity { DiscordId = Bob, Username = "bob" };
        var carol = new UserEntity { DiscordId = Carol, Username = "carol" };
        var bot = new UserEntity { DiscordId = BotUser, Username = "bot", IsBot = true };
        _db.Guilds.Add(guild);
        _db.Users.AddRange(alice, bob, carol, bot);
        await _db.SaveChangesAsync();

        _db.Channels.AddRange(
            new ChannelEntity { DiscordId = 555UL, GuildId = guild.Id, Name = "general", Type = ChannelType.Text },
            new ChannelEntity
            {
                DiscordId = 556UL,
                GuildId = guild.Id,
                Name = "gone",
                Type = ChannelType.Text,
                IsDeleted = true,
                DeletedAtUtc = DateTime.UtcNow,
            });
        _db.Members.Add(new MemberEntity { UserId = alice.Id, GuildId = guild.Id });
        _db.RawEventLogs.Add(new RawEventLogEntity
        {
            EventType = "PresenceUpdated",
            GuildDiscordId = GuildSf,
            EventJson = "{}",
            JsonSizeBytes = 2,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-3),
        });

        var t = DateTime.UtcNow;
        // Two presence rows for Alice: latest (desktop online) must win.
        _db.PresenceEvents.AddRange(
            Presence(Alice, desktop: 1, at: t.AddMinutes(-10)),
            Presence(Alice, desktop: 3, at: t.AddMinutes(-1)),
            Presence(Bob, mobile: 1, at: t.AddMinutes(-2)),
            Presence(Carol, desktop: 0, at: t.AddMinutes(-2)),
            Presence(BotUser, desktop: 3, at: t.AddMinutes(-2)));

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static PresenceEventEntity Presence(
        ulong user, DateTime at, int desktop = 0, int mobile = 0, int web = 0) => new()
        {
            UserDiscordId = user,
            GuildDiscordId = GuildSf,
            EventType = PresenceEventType.Updated,
            DesktopStatusAfter = desktop,
            MobileStatusAfter = mobile,
            WebStatusAfter = web,
            ReceivedAtUtc = at,
            EventTimestampUtc = at,
        };

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
