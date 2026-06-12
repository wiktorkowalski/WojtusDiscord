using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

// Per-class Postgres container (IClassFixture) so the seeded downtime interval can't leak into other voice tests.
public sealed class VoiceDowntimeAndDailyTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildSf = 742554855180206203UL;
    private const ulong Alice = 100UL;
    private const ulong Bob = 200UL;
    private const ulong ChannelSf = 555UL;

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.VoiceStateEvents.ExecuteDeleteAsync();
        await _db.BotDowntimeIntervals.ExecuteDeleteAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task VoiceLeaderboard_SubtractsDowntimeOverlapWithoutCapping()
    {
        var controller = new StatsController(_db);

        var voice = (await controller.VoiceLeaderboard(default)).Value!;

        // Bob: a full 120-min session with no overlapping outage -> 120 (proves no cap;
        // a 30-min cap would truncate this to 30). Alice: a 120-min session straddling a
        // 60-min outage -> 60 (proves overlap is SUBTRACTED, not the whole segment dropped
        // to 0 nor capped to <=30).
        var bob = voice.Single(v => v.UserDiscordId == Bob);
        Assert.Equal(120, bob.Minutes);

        var alice = voice.Single(v => v.UserDiscordId == Alice);
        Assert.Equal(60, alice.Minutes);
    }

    [Fact]
    public async Task Profile_VoiceMinutes_SubtractsDowntimeOverlap()
    {
        var controller = new PeopleController(_db);

        var dto = (await controller.Profile(unchecked((long)Alice), default)).Value!;

        // C# sessionization path mirrors the SQL: 120-min session minus 60-min outage = 60.
        Assert.Equal(60, dto.VoiceMinutes);
    }

    [Fact]
    public async Task Overview_MessagesDaily_IsDense30DayZeroFilledSeries()
    {
        var controller = new StatsController(_db);

        var overview = (await controller.Overview(default)).Value!;

        // Always 30 buckets (today + prior 29 CET days), regardless of which days have data.
        Assert.Equal(30, overview.MessagesDaily.Count);
        // 3 today + 2 five days ago = 5 total across the window.
        Assert.Equal(5, overview.MessagesDaily.Sum(p => p.Count));
        // Days between the two seeded days are present as explicit zeros (dense series).
        Assert.Contains(overview.MessagesDaily, p => p.Count == 0);
        Assert.Contains(overview.MessagesDaily, p => p.Count == 3);
        Assert.Contains(overview.MessagesDaily, p => p.Count == 2);
    }

    private async Task SeedAsync()
    {
        var guild = new GuildEntity { DiscordId = GuildSf, Name = "G" };
        var alice = new UserEntity { DiscordId = Alice, Username = "alice" };
        var bob = new UserEntity { DiscordId = Bob, Username = "bob" };
        _db.Guilds.Add(guild);
        _db.Users.AddRange(alice, bob);
        await _db.SaveChangesAsync();

        var channel = new ChannelEntity { DiscordId = ChannelSf, GuildId = guild.Id, Name = "general", Type = ChannelType.Text };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        // Alice: 120-min voice session two days ago, straddling a 60-min bot outage.
        var aliceStart = now.AddDays(-2);
        _db.VoiceStateEvents.AddRange(
            Voice(Alice, null, ChannelSf, VoiceEventType.Joined, aliceStart),
            Voice(Alice, ChannelSf, null, VoiceEventType.Left, aliceStart.AddMinutes(120)));
        _db.BotDowntimeIntervals.Add(new BotDowntimeIntervalEntity
        {
            StartedAtUtc = aliceStart.AddMinutes(30),
            EndedAtUtc = aliceStart.AddMinutes(90),
        });

        // Bob: 120-min session four days ago, nowhere near the outage.
        var bobStart = now.AddDays(-4);
        _db.VoiceStateEvents.AddRange(
            Voice(Bob, null, ChannelSf, VoiceEventType.Joined, bobStart),
            Voice(Bob, ChannelSf, null, VoiceEventType.Left, bobStart.AddMinutes(120)));

        // Messages for the dense daily series: 3 today + 2 five days ago, nothing between.
        _db.Messages.AddRange(
            Message(alice.Id, channel.Id, guild.Id, 1UL, now),
            Message(alice.Id, channel.Id, guild.Id, 2UL, now),
            Message(alice.Id, channel.Id, guild.Id, 3UL, now),
            Message(alice.Id, channel.Id, guild.Id, 4UL, now.AddDays(-5)),
            Message(alice.Id, channel.Id, guild.Id, 5UL, now.AddDays(-5)));

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static MessageEntity Message(Guid authorId, Guid channelId, Guid guildId, ulong discordId, DateTime at) => new MessageEntity
    {
        DiscordId = discordId,
        AuthorId = authorId,
        ChannelId = channelId,
        GuildId = guildId,
        Content = "hi",
        CreatedAtUtc = at,
    };

    private static VoiceStateEventEntity Voice(
        ulong user, ulong? before, ulong? after, VoiceEventType type, DateTime at) => new VoiceStateEventEntity
        {
            UserDiscordId = user,
            GuildDiscordId = GuildSf,
            ChannelDiscordIdBefore = before,
            ChannelDiscordIdAfter = after,
            EventType = type,
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
