using System.Text.Json;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class StatsControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong Alice = 100UL;
    private const ulong Bob = 200UL;
    private const ulong ChannelSf = 555UL;

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.PresenceEvents.ExecuteDeleteAsync();
        await _db.ReactionEvents.ExecuteDeleteAsync();
        await _db.VoiceStateEvents.ExecuteDeleteAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        await _db.Activities.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task TopMessages_TwoAuthorsSeeded_RanksAuthorsByCount()
    {
        var controller = new StatsController(_db);
        var top = (await controller.TopMessages(default)).Value!;

        Assert.Equal("alice", top[0].Username);
        Assert.Equal(2, top[0].Count);
        Assert.Equal("bob", top[1].Username);
        Assert.Equal(1, top[1].Count);
    }

    [Fact]
    public async Task VoiceLeaderboard_SeededSessions_SumsSessionMinutes()
    {
        var controller = new StatsController(_db);
        var voice = (await controller.VoiceLeaderboard(default)).Value!;

        var alice = Assert.Single(voice);
        Assert.Equal(Alice, alice.UserDiscordId);
        Assert.Equal(10, alice.Minutes); // 12:00 join -> 12:10 leave
    }

    [Fact]
    public async Task TopEmojis_CustomAndUnicodeReactions_CountsAndFlagsCustom()
    {
        var controller = new StatsController(_db);
        var emojis = (await controller.TopEmojis(default)).Value!;

        var thumbs = emojis.Single(e => e.EmoteName == "👍");
        Assert.Equal(2, thumbs.Count);
        Assert.False(thumbs.IsCustom);

        var custom = emojis.Single(e => e.EmoteName == "megalul");
        Assert.True(custom.IsCustom);
    }

    [Fact]
    public async Task ChannelActivity_SeededMessagesAndReactions_CountsBoth()
    {
        var controller = new StatsController(_db);
        var channels = (await controller.ChannelActivity(default)).Value!;

        var general = Assert.Single(channels);
        Assert.Equal("general", general.ChannelName);
        Assert.Equal(3, general.MessageCount);
        Assert.Equal(3, general.ReactionCount);
    }

    [Fact]
    public async Task TopReactionsGiven_TwoReactorsSeeded_RanksReactorsByCount()
    {
        var controller = new StatsController(_db);
        var given = (await controller.TopReactionsGiven(default)).Value!;

        var alice = Assert.Single(given);
        Assert.Equal(Alice, alice.UserDiscordId);
        Assert.Equal("alice", alice.Username); // resolved via the correlated user lookup
        Assert.Equal(3, alice.Count);          // 👍x2 + megalul x1
    }

    [Fact]
    public async Task TopReactionsReceived_ReactionsOnAuthoredMessages_RanksAuthors()
    {
        var controller = new StatsController(_db);
        var received = (await controller.TopReactionsReceived(default)).Value!;

        // All 3 reactions point at message 1 (alice's), so alice receives 3.
        var alice = Assert.Single(received);
        Assert.Equal(Alice, alice.UserDiscordId);
        Assert.Equal(3, alice.Count);
    }

    [Fact]
    public async Task TopActivities_SeededActivities_CountsByNameDescending()
    {
        var controller = new StatsController(_db);
        var activities = (await controller.TopActivities(default)).Value!;

        Assert.Equal("Visual Studio Code", activities[0].Name);
        Assert.Equal(2, activities[0].Count);
        Assert.Equal("Spotify", activities[1].Name);
        Assert.Equal(1, activities[1].Count);
    }

    [Fact]
    public async Task Overview_SeededEvents_AggregatesTotals()
    {
        var controller = new StatsController(_db);

        var o = (await controller.Overview(new FixedMemeIndexSummaryReader(), default)).Value!;

        Assert.Equal(3, o.TotalMessages);
        Assert.Equal(3, o.Messages.Total);
        Assert.Equal(3, o.TotalReactions);
        Assert.Equal(10, o.VoiceMinutes);
        Assert.Equal(2, o.TotalUsers);
        Assert.Equal(1, o.TotalChannels);
        Assert.Equal("alice", o.TopChatter!.Username);
        Assert.Equal("general", o.TopChannel!.ChannelName);
        Assert.NotEmpty(o.TopEmojis);
    }

    [Fact]
    public async Task VolumeByType_MixedEventTypes_RanksByCount()
    {
        var controller = new StatsController(_db);
        var volume = (await controller.VolumeByType(default)).Value!;

        Assert.Equal("MessageCreated", volume[0].EventType);
        Assert.Equal(2, volume[0].Count);
    }

    // #393: the dashboard reads snowflakes as strings (a JS number loses precision past 2^53),
    // so the new Community fields go through the real JSON pipeline here.
    [Fact]
    public async Task Community_NewWindowFields_SerializeSnowflakesAsStrings()
    {
        var controller = new StatsController(_db);

        var dto = (await controller.Community("all", default)).Value!;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, DashboardJson.CreateOptions()));
        var root = json.RootElement;

        var channel = root.GetProperty("channels")[0];
        Assert.Equal(JsonValueKind.String, channel.GetProperty("channelDiscordId").ValueKind);
        Assert.Equal("555", channel.GetProperty("channelDiscordId").GetString());
        Assert.Equal(3, channel.GetProperty("messageCount").GetInt64());
        Assert.Equal(3, channel.GetProperty("reactionCount").GetInt64());

        var emotes = root.GetProperty("topEmotes").EnumerateArray().ToList();
        var custom = emotes.Single(e => e.GetProperty("emoteName").GetString() == "megalul");
        Assert.Equal(JsonValueKind.String, custom.GetProperty("emoteDiscordId").ValueKind);
        Assert.Equal("999", custom.GetProperty("emoteDiscordId").GetString());
        Assert.True(custom.GetProperty("isCustom").GetBoolean());
        var unicode = emotes.Single(e => e.GetProperty("emoteName").GetString() == "👍");
        Assert.Equal(JsonValueKind.Null, unicode.GetProperty("emoteDiscordId").ValueKind);

        var given = root.GetProperty("leaderboards").GetProperty("reactionsGiven")[0];
        Assert.Equal(JsonValueKind.String, given.GetProperty("userDiscordId").ValueKind);
        Assert.Equal("100", given.GetProperty("userDiscordId").GetString());
        Assert.Equal(3, given.GetProperty("value").GetInt64());

        var game = Assert.Single(root.GetProperty("topActivities").EnumerateArray());
        Assert.Equal("Visual Studio Code", game.GetProperty("name").GetString());
        Assert.Equal(30, game.GetProperty("minutes").GetInt64());
        Assert.Equal(1, game.GetProperty("players").GetInt64());

        Assert.Equal(JsonValueKind.Array, root.GetProperty("heatmap").ValueKind);
        Assert.Equal(30, root.GetProperty("heatmapDays").GetInt32());
    }

    private async Task SeedAsync()
    {
        var guild = new GuildEntity { DiscordId = 742554855180206203UL, Name = "G" };
        var alice = new UserEntity { DiscordId = Alice, Username = "alice" };
        var bob = new UserEntity { DiscordId = Bob, Username = "bob" };
        _db.Guilds.Add(guild);
        _db.Users.AddRange(alice, bob);
        await _db.SaveChangesAsync();

        var channel = new ChannelEntity { DiscordId = ChannelSf, GuildId = guild.Id, Name = "general", Type = ChannelType.Text };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();

        // 3 messages: alice x2, bob x1
        _db.Messages.AddRange(
            Message(alice.Id, channel.Id, guild.Id, 1UL),
            Message(alice.Id, channel.Id, guild.Id, 2UL),
            Message(bob.Id, channel.Id, guild.Id, 3UL));

        // Voice: alice joins 12:00, leaves 12:10 -> 10 minutes
        var t = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.VoiceStateEvents.AddRange(
            Voice(Alice, guild.DiscordId, null, ChannelSf, VoiceEventType.Joined, t),
            Voice(Alice, guild.DiscordId, ChannelSf, null, VoiceEventType.Left, t.AddMinutes(10)));

        // Reactions given by alice: 👍 x2 (unicode), megalul x1 (custom)
        _db.ReactionEvents.AddRange(
            Reaction(Alice, guild.DiscordId, ChannelSf, "👍", null),
            Reaction(Alice, guild.DiscordId, ChannelSf, "👍", null),
            Reaction(Alice, guild.DiscordId, ChannelSf, "megalul", 999UL));

        // Raw events for volume
        _db.RawEventLogs.AddRange(
            Raw("MessageCreated", t), Raw("MessageCreated", t.AddMinutes(1)), Raw("PresenceUpdated", t.AddMinutes(2)));

        // Activities: "Visual Studio Code" x2, "Spotify" x1.
        _db.Activities.AddRange(
            new ActivityEntity { UserId = alice.Id, Name = "Visual Studio Code", ActivityType = 0 },
            new ActivityEntity { UserId = alice.Id, Name = "Visual Studio Code", ActivityType = 0 },
            new ActivityEntity { UserId = bob.Id, Name = "Spotify", ActivityType = 2 });

        // Presence: alice plays for 30 minutes (feeds Community.TopActivities).
        _db.PresenceEvents.AddRange(
            new PresenceEventEntity
            {
                UserDiscordId = Alice,
                GuildDiscordId = guild.DiscordId,
                ActivitiesAfterJson = """[{"name":"Visual Studio Code","type":0,"streamUrl":null}]""",
                EventTimestampUtc = t,
                ReceivedAtUtc = t,
            },
            new PresenceEventEntity
            {
                UserDiscordId = Alice,
                GuildDiscordId = guild.DiscordId,
                EventTimestampUtc = t.AddMinutes(30),
                ReceivedAtUtc = t.AddMinutes(30),
            });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static MessageEntity Message(Guid authorId, Guid channelId, Guid guildId, ulong discordId) => new MessageEntity
    {
        DiscordId = discordId,
        AuthorId = authorId,
        ChannelId = channelId,
        GuildId = guildId,
        Content = "hi",
        CreatedAtUtc = new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc),
    };

    private static VoiceStateEventEntity Voice(
        ulong user, ulong guild, ulong? before, ulong? after, VoiceEventType type, DateTime at) => new VoiceStateEventEntity
        {
            UserDiscordId = user,
            GuildDiscordId = guild,
            ChannelDiscordIdBefore = before,
            ChannelDiscordIdAfter = after,
            EventType = type,
            ReceivedAtUtc = at,
            EventTimestampUtc = at,
        };

    private static ReactionEventEntity Reaction(
        ulong user, ulong guild, ulong channel, string emote, ulong? emoteId) => new ReactionEventEntity
        {
            UserDiscordId = user,
            GuildDiscordId = guild,
            ChannelDiscordId = channel,
            MessageDiscordId = 1UL,
            EmoteName = emote,
            EmoteDiscordId = emoteId,
            EventType = ReactionEventType.Added,
            // A few minutes back: the Community window ends at the DB now(), and the
            // container clock can sit slightly behind the host clock.
            ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            EventTimestampUtc = DateTime.UtcNow.AddMinutes(-5),
        };

    private static RawEventLogEntity Raw(string type, DateTime at) => new RawEventLogEntity
    {
        EventType = type,
        GuildDiscordId = 742554855180206203UL,
        EventJson = "{}",
        JsonSizeBytes = 2,
        ReceivedAtUtc = at,
    };

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
