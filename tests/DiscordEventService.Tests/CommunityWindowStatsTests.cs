using System.Text.Json;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

// #393: the range-scoped lists on GET /api/stats/community (top emotes, reactions given,
// channels, top activities) and the fixed 30-day heatmap.
public sealed class CommunityWindowStatsTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildSf = 742554855180206203UL;
    private const ulong Alice = 100UL;
    private const ulong Bob = 200UL;
    private const ulong BotUser = 300UL;
    private const ulong Stranger = 400UL; // reacts, but has no users row
    private const ulong General = 555UL;
    private const ulong Memes = 556UL;
    private const ulong Quiet = 557UL;
    private const ulong TieLow = 600UL;
    private const ulong TieHigh = 601UL;

    private const int Playing = 0;
    private const int Listening = 2;
    private const int Custom = 4;

    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private readonly DateTime _now = DateTime.UtcNow;
    private readonly Dictionary<ulong, Guid> _channelIds = [];

    private DiscordDbContext _db = null!;
    private Guid _guildId;
    private Guid _aliceId;
    private Guid _bobId;
    private ulong _nextMessageSf = 1UL;

    // Inside / outside the 7-day window. The controller anchors the window on the DB now(),
    // which is later than _now, so "outside" stays outside.
    private DateTime InWeek => _now.AddDays(-6).AddHours(-23);
    private DateTime JustBeforeWeek => _now.AddDays(-7).AddMinutes(-1);

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.BotDowntimeIntervals.ExecuteDeleteAsync();
        await _db.PresenceEvents.ExecuteDeleteAsync();
        await _db.VoiceStateEvents.ExecuteDeleteAsync();
        await _db.ReactionEvents.ExecuteDeleteAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedBaseAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task TopEmotes_Week_CountsPresentReactionsInsideTheWindowOnly()
    {
        await SeedReactionsAsync();

        var week = await CommunityAsync("week");

        // "old" (x5, just before the window) and "gone" (Removed) are absent.
        // aaa and bbb tie on 1: emote name breaks the tie.
        Assert.Equal(["👍", "aaa", "bbb"], week.TopEmotes.Select(e => e.EmoteName));
        Assert.Equal([3L, 1L, 1L], week.TopEmotes.Select(e => e.Count));
        Assert.True(week.TopEmotes[1].IsCustom);
        Assert.Equal(999UL, week.TopEmotes[1].EmoteDiscordId);
        Assert.False(week.TopEmotes[0].IsCustom);
    }

    [Fact]
    public async Task TopEmotes_All_IncludesReactionsBeforeTheWeek()
    {
        await SeedReactionsAsync();

        var all = await CommunityAsync("all");

        Assert.Equal("old", all.TopEmotes[0].EmoteName);
        Assert.Equal(5, all.TopEmotes[0].Count);
    }

    [Fact]
    public async Task ReactionsGiven_Week_RanksReactorsInsideTheWindowWithIdTiebreak()
    {
        await SeedReactionsAsync();

        var week = await CommunityAsync("week");
        var given = week.Leaderboards.ReactionsGiven;

        // alice 2 and bob 2 tie: the lower snowflake goes first. Bob's 5 older reactions and
        // alice's Removed reaction do not count. The stranger has no users row: null username.
        Assert.Equal([Alice, Bob, Stranger], given.Select(g => g.UserDiscordId));
        Assert.Equal([2L, 2L, 1L], given.Select(g => g.Value));
        Assert.Equal(["alice", "bob", null], given.Select(g => g.Username));

        var all = await CommunityAsync("all");
        Assert.Equal(Bob, all.Leaderboards.ReactionsGiven[0].UserDiscordId);
        Assert.Equal(7, all.Leaderboards.ReactionsGiven[0].Value);
    }

    [Fact]
    public async Task Channels_Week_CountsMessagesAndReactionsInsideTheWindowWithTiebreaks()
    {
        await SeedReactionsAsync();
        _db.Messages.AddRange(
            Message(_aliceId, General, InWeek),
            Message(_bobId, General, InWeek),
            Message(_aliceId, Memes, InWeek),
            Message(_bobId, Memes, InWeek),
            Message(_aliceId, TieHigh, InWeek),
            Message(_aliceId, TieLow, InWeek),
            // Quiet: its only message and its reactions are just before the window.
            Message(_aliceId, Quiet, JustBeforeWeek));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        // general and memes tie on 2 messages: reactions (3 vs 2) break the tie.
        // The two tie channels match on both counts: the lower snowflake goes first.
        Assert.Equal([General, Memes, TieLow, TieHigh], week.Channels.Select(c => c.ChannelDiscordId));
        Assert.Equal([2L, 2L, 1L, 1L], week.Channels.Select(c => c.MessageCount));
        Assert.Equal([3L, 2L, 0L, 0L], week.Channels.Select(c => c.ReactionCount));

        var all = await CommunityAsync("all");
        var quiet = Assert.Single(all.Channels, c => c.ChannelDiscordId == Quiet);
        Assert.Equal("quiet", quiet.ChannelName);
        Assert.Equal(1, quiet.MessageCount);
        Assert.Equal(5, quiet.ReactionCount);
    }

    [Fact]
    public async Task Channels_ReactionsOnly_ChannelIsListed()
    {
        _db.ReactionEvents.Add(Reaction(Alice, "👍", null, Quiet, InWeek));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        var quiet = Assert.Single(week.Channels);
        Assert.Equal(Quiet, quiet.ChannelDiscordId);
        Assert.Equal(0, quiet.MessageCount);
        Assert.Equal(1, quiet.ReactionCount);
    }

    [Fact]
    public async Task TopActivities_Week_SessionizesPresenceWithoutDoubleCounting()
    {
        await SeedPresenceAsync();

        var week = await CommunityAsync("week");
        var games = week.TopActivities;

        // Witcher: alice 90 min (a duplicated event, a duplicated array entry and two adjacent
        // segments count each minute once) + bob 30 min. The bot's 10 h do not count.
        // Celeste and Hades tie on 90: the name breaks the tie. Hades is 120 min of presence
        // minus 30 min of bot downtime. Doom starts before the window: only the part inside counts.
        Assert.Equal(["The Witcher 3", "Celeste", "Hades", "Doom"], games.Select(g => g.Name));
        Assert.Equal(new CommunityActivityDto("The Witcher 3", 120, 2), games[0]);
        Assert.Equal(new CommunityActivityDto("Celeste", 90, 1), games[1]);
        Assert.Equal(new CommunityActivityDto("Hades", 90, 1), games[2]);
        Assert.InRange(games[3].Minutes, 55, 60);
        Assert.Equal(1, games[3].Players);
    }

    [Fact]
    public async Task TopActivities_Month_IncludesSessionsBeforeTheWeek()
    {
        await SeedPresenceAsync();

        var month = await CommunityAsync("month");
        var games = month.TopActivities;

        // Doom is whole now: 1 day + 1 h. Quake (a full day, 9 to 8 days ago) enters.
        Assert.Equal(["Doom", "Quake", "The Witcher 3", "Celeste", "Hades"], games.Select(g => g.Name));
        Assert.Equal(1500, games[0].Minutes);
        Assert.Equal(1440, games[1].Minutes);
    }

    [Fact]
    public async Task TopActivities_OpenDowntimeInterval_SubtractsUpToNow()
    {
        var start = _now.AddHours(-3);
        _db.PresenceEvents.AddRange(
            Presence(Alice, start, ("Hades", Playing)),
            Presence(Alice, start.AddHours(2)));
        // Open interval from 1 h into the session: only the first hour is credited.
        _db.BotDowntimeIntervals.Add(Downtime(start.AddHours(1), null));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        Assert.Equal(new CommunityActivityDto("Hades", 60, 1), Assert.Single(week.TopActivities));
    }

    [Fact]
    public async Task TopActivities_Week_ClipsASegmentThatStartsLongBeforeTheWindow()
    {
        _db.PresenceEvents.AddRange(
            // Older events of the same user: only the last one before the window opens the segment.
            Presence(Alice, _now.AddDays(-30), ("Old Game", Playing)),
            Presence(Alice, _now.AddDays(-25)),
            Presence(Alice, _now.AddDays(-20), ("Factorio", Playing)),
            Presence(Alice, _now.AddDays(-7).AddHours(2)),
            // Bob has no event in the window: his last segment before it is open, not counted.
            Presence(Bob, _now.AddDays(-21)),
            Presence(Bob, _now.AddDays(-20), ("Quake", Playing)));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        // 13 days of Factorio, of which the last 2 h are inside the window.
        var factorio = Assert.Single(week.TopActivities);
        Assert.Equal("Factorio", factorio.Name);
        Assert.InRange(factorio.Minutes, 115, 120);
        Assert.Equal(1, factorio.Players);
    }

    [Fact]
    public async Task VoiceMinutes_Week_CountASegmentInTheWindowOfItsStart()
    {
        _db.VoiceStateEvents.AddRange(
            // Starts 10 min before the window, ends 50 min into it: all 60 min belong to prev.
            Voice(Alice, General, _now.AddDays(-7).AddMinutes(-10)),
            Voice(Alice, null, _now.AddDays(-7).AddMinutes(50)),
            // Starts in the window, ends after its end: counted in full.
            Voice(Alice, General, _now.AddMinutes(-20)),
            Voice(Alice, null, _now.AddMinutes(15)));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        Assert.Equal(35, week.Metrics.VoiceMinutes.Value);
        Assert.Equal(60, week.Metrics.VoiceMinutes.Prev);
        Assert.Equal(7, week.Metrics.VoiceMinutes.Spark.Count);
        Assert.Equal(35, week.Metrics.VoiceMinutes.Spark.Sum());
        var leader = Assert.Single(week.Leaderboards.Voice);
        Assert.Equal(Alice, leader.UserDiscordId);
        Assert.Equal(35, leader.Value);

        var all = await CommunityAsync("all");
        Assert.Equal(95, all.Metrics.VoiceMinutes.Value);
        Assert.Null(all.Metrics.VoiceMinutes.Prev);
        Assert.Equal(95, all.Metrics.VoiceMinutes.Spark.Sum());
        Assert.Equal(95, Assert.Single(all.Leaderboards.Voice).Value);
    }

    [Fact]
    public async Task OnlineMinutes_Week_CountASegmentInTheWindowOfItsStart()
    {
        _db.PresenceEvents.AddRange(
            // Starts 10 min before the window, ends 10 min into it: all 20 min belong to prev.
            Status(Alice, _now.AddDays(-7).AddMinutes(-10), online: true),
            Status(Alice, _now.AddDays(-7).AddMinutes(10), online: false),
            // Starts in the window, ends after its end: counted in full.
            Status(Alice, _now.AddMinutes(-10), online: true),
            Status(Alice, _now.AddMinutes(15), online: false));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        Assert.Equal(25, week.Metrics.OnlineMinutes.Value);
        Assert.Equal(20, week.Metrics.OnlineMinutes.Prev);
        Assert.Equal(7, week.Metrics.OnlineMinutes.Spark.Count);
        Assert.Equal(25, week.Metrics.OnlineMinutes.Spark.Sum());

        var all = await CommunityAsync("all");
        Assert.Equal(45, all.Metrics.OnlineMinutes.Value);
        Assert.Null(all.Metrics.OnlineMinutes.Prev);
        Assert.Equal(45, all.Metrics.OnlineMinutes.Spark.Sum());
    }

    [Fact]
    public async Task OnlineMinutes_OverlappingDowntime_DropsASegmentOnlyTheEarlierIntervalCovers()
    {
        var t = _now.AddDays(-1);
        _db.BotDowntimeIntervals.AddRange(
            Downtime(t, t.AddHours(3)),
            // Starts later and ends sooner: the last interval before the 2 h segment, and it
            // does not cover it. Only the longer, earlier one does.
            Downtime(t.AddHours(1), t.AddHours(1).AddMinutes(30)));
        _db.PresenceEvents.AddRange(
            // Ends exactly where the downtime starts: no overlap, kept (20 min).
            Status(Alice, t.AddMinutes(-20), online: true),
            Status(Alice, t, online: false),
            // Inside the long interval, after the short one: dropped.
            Status(Alice, t.AddHours(2), online: true),
            Status(Alice, t.AddHours(2).AddMinutes(20), online: false),
            // Starts exactly where the downtime ends: no overlap, kept (10 min).
            Status(Alice, t.AddHours(3), online: true),
            Status(Alice, t.AddHours(3).AddMinutes(10), online: false),
            // Clear of every interval: kept (10 min).
            Status(Alice, t.AddHours(4), online: true),
            Status(Alice, t.AddHours(4).AddMinutes(10), online: false));
        await _db.SaveChangesAsync();

        var week = await CommunityAsync("week");

        Assert.Equal(40, week.Metrics.OnlineMinutes.Value);
        Assert.Equal(40, week.Metrics.OnlineMinutes.Spark.Sum());
    }

    [Theory]
    [InlineData("week")]
    [InlineData("month")]
    [InlineData("all")]
    public async Task Heatmap_AnyRange_CoversTheLast30GuildLocalDays(string range)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(_now, Warsaw).Date;
        var yesterdayEvening = today.AddDays(-1).AddHours(21).AddMinutes(30);
        var firstDay = today.AddDays(-29).AddMinutes(30);         // 00:30 on the oldest day in the window
        var dayBefore = today.AddDays(-30).AddHours(23).AddMinutes(30); // 23:30 on the day before it
        _db.Messages.AddRange(
            Message(_aliceId, General, LocalToUtc(yesterdayEvening)),
            Message(_bobId, General, LocalToUtc(yesterdayEvening)),
            Message(_aliceId, General, LocalToUtc(firstDay)),
            Message(_aliceId, General, LocalToUtc(dayBefore)));
        await _db.SaveChangesAsync();

        var dto = await CommunityAsync(range);

        Assert.Equal(30, dto.HeatmapDays);
        Assert.Equal(2, dto.Heatmap.Count);
        Assert.Contains(new HeatmapCellDto((int)yesterdayEvening.DayOfWeek, 21, 2), dto.Heatmap);
        Assert.Contains(new HeatmapCellDto((int)firstDay.DayOfWeek, 0, 1), dto.Heatmap);

        // The all-time endpoint keeps the message outside the window.
        var allTime = (await new StatsController(_db).Heatmap(default)).Value!;
        Assert.Contains(new HeatmapCellDto((int)dayBefore.DayOfWeek, 23, 1), allTime);
        Assert.Equal(4, allTime.Sum(c => c.Count));
    }

    private async Task<CommunityDto> CommunityAsync(string range) =>
        (await new StatsController(_db).Community(range, default)).Value!;

    private async Task SeedBaseAsync()
    {
        var guild = new GuildEntity { DiscordId = GuildSf, Name = "G" };
        var alice = new UserEntity { DiscordId = Alice, Username = "alice" };
        var bob = new UserEntity { DiscordId = Bob, Username = "bob" };
        var bot = new UserEntity { DiscordId = BotUser, Username = "mc-status", IsBot = true };
        _db.Guilds.Add(guild);
        _db.Users.AddRange(alice, bob, bot);
        await _db.SaveChangesAsync();
        _guildId = guild.Id;
        _aliceId = alice.Id;
        _bobId = bob.Id;

        (ulong Sf, string Name)[] channels =
            [(General, "general"), (Memes, "memes"), (Quiet, "quiet"), (TieLow, "tie-low"), (TieHigh, "tie-high")];
        foreach (var (sf, name) in channels)
        {
            var channel = new ChannelEntity { DiscordId = sf, GuildId = guild.Id, Name = name, Type = ChannelType.Text };
            _db.Channels.Add(channel);
            await _db.SaveChangesAsync();
            _channelIds[sf] = channel.Id;
        }

        // "Launch" for range=all: 40 days ago.
        _db.RawEventLogs.Add(new RawEventLogEntity
        {
            EventType = "MessageCreated",
            GuildDiscordId = GuildSf,
            EventJson = "{}",
            JsonSizeBytes = 2,
            ReceivedAtUtc = _now.AddDays(-40),
        });
        await _db.SaveChangesAsync();
    }

    // In the week: alice 👍 x2 (general), bob aaa + bbb (memes), stranger 👍 (general),
    // alice "gone" Removed (general). Just before the week: bob "old" x5 (quiet).
    private async Task SeedReactionsAsync()
    {
        _db.ReactionEvents.AddRange(
            Reaction(Alice, "👍", null, General, InWeek),
            Reaction(Alice, "👍", null, General, InWeek),
            Reaction(Bob, "bbb", null, Memes, InWeek),
            Reaction(Bob, "aaa", 999UL, Memes, InWeek),
            Reaction(Stranger, "👍", null, General, InWeek),
            Reaction(Alice, "gone", null, General, InWeek, ReactionEventType.Removed));
        _db.ReactionEvents.AddRange(
            Enumerable.Range(0, 5).Select(_ => Reaction(Bob, "old", null, Quiet, JustBeforeWeek)));
        await _db.SaveChangesAsync();
    }

    private async Task SeedPresenceAsync()
    {
        var t = _now.AddDays(-2);

        _db.PresenceEvents.AddRange(
            // Doom: 8 days ago until 1 h into the week window.
            Presence(Alice, _now.AddDays(-8), ("Doom", Playing)),
            Presence(Alice, _now.AddDays(-7).AddHours(1)),
            // Witcher: the same event twice (one per shared guild), the game twice in one
            // array, Spotify + custom status alongside, then a second adjacent segment.
            Presence(Alice, t, ("The Witcher 3", Playing), ("The Witcher 3", Playing), ("Spotify", Listening), ("Custom Status", Custom)),
            Presence(Alice, t, ("The Witcher 3", Playing), ("The Witcher 3", Playing), ("Spotify", Listening), ("Custom Status", Custom)),
            Presence(Alice, t.AddMinutes(60), ("The Witcher 3", Playing)),
            Presence(Alice, t.AddMinutes(90)),
            // Hades: 2 h of presence, 30 min of it inside a bot downtime interval.
            Presence(Alice, t.AddHours(3), ("Hades", Playing)),
            Presence(Alice, t.AddHours(5)),
            // Noise: server-status names and non-game names, even when typed Playing.
            Presence(Alice, t.AddHours(8), ("Playing 3/10", Playing), ("Offline", Playing), ("Spotify", Playing), ("Custom Status", Playing)),
            Presence(Alice, t.AddHours(9)),

            // Quake: entirely before the week window.
            Presence(Bob, _now.AddDays(-9), ("Quake", Playing)),
            Presence(Bob, _now.AddDays(-8)),
            Presence(Bob, t, ("The Witcher 3", Playing)),
            Presence(Bob, t.AddMinutes(30)),
            Presence(Bob, t.AddHours(6), ("Celeste", Playing)),
            Presence(Bob, t.AddHours(7).AddMinutes(30)),
            // Open segment (no later event): not counted.
            Presence(Bob, _now.AddHours(-1), ("Open Game", Playing)),

            // A bot account "plays" for 10 h: not counted.
            Presence(BotUser, t, ("The Witcher 3", Playing)),
            Presence(BotUser, t.AddHours(10)));

        _db.BotDowntimeIntervals.Add(Downtime(t.AddHours(3).AddMinutes(30), t.AddHours(4)));
        await _db.SaveChangesAsync();
    }

    private MessageEntity Message(Guid authorId, ulong channel, DateTime at) => new MessageEntity
    {
        DiscordId = _nextMessageSf++,
        AuthorId = authorId,
        ChannelId = _channelIds[channel],
        GuildId = _guildId,
        Content = "hi",
        CreatedAtUtc = at,
    };

    private static ReactionEventEntity Reaction(
        ulong user, string emote, ulong? emoteId, ulong channel, DateTime at,
        ReactionEventType type = ReactionEventType.Added) => new ReactionEventEntity
        {
            UserDiscordId = user,
            GuildDiscordId = GuildSf,
            ChannelDiscordId = channel,
            MessageDiscordId = 1UL,
            EmoteName = emote,
            EmoteDiscordId = emoteId,
            EventType = type,
            ReceivedAtUtc = at,
            EventTimestampUtc = at,
        };

    // Mirrors the JSON PresenceEventHandler writes: camelCase name/type/streamUrl, null when empty.
    private static PresenceEventEntity Presence(ulong user, DateTime at, params (string Name, int Type)[] activities) =>
        new PresenceEventEntity
        {
            UserDiscordId = user,
            GuildDiscordId = GuildSf,
            ActivitiesAfterJson = activities.Length == 0
                ? null
                : JsonSerializer.Serialize(activities.Select(a => new { name = a.Name, type = a.Type, streamUrl = (string?)null })),
            EventTimestampUtc = at,
            ReceivedAtUtc = at,
        };

    // A presence event that only carries the overall status (1 = online, 0 = offline).
    private static PresenceEventEntity Status(ulong user, DateTime at, bool online) => new PresenceEventEntity
    {
        UserDiscordId = user,
        GuildDiscordId = GuildSf,
        DesktopStatusAfter = online ? 1 : 0,
        EventTimestampUtc = at,
        ReceivedAtUtc = at,
    };

    // channelAfter = null is a leave.
    private static VoiceStateEventEntity Voice(ulong user, ulong? channelAfter, DateTime at) => new VoiceStateEventEntity
    {
        UserDiscordId = user,
        GuildDiscordId = GuildSf,
        ChannelDiscordIdBefore = channelAfter is null ? General : null,
        ChannelDiscordIdAfter = channelAfter,
        EventType = channelAfter is null ? VoiceEventType.Left : VoiceEventType.Joined,
        EventTimestampUtc = at,
        ReceivedAtUtc = at,
    };

    private static BotDowntimeIntervalEntity Downtime(DateTime start, DateTime? end) => new BotDowntimeIntervalEntity
    {
        StartedAtUtc = start,
        EndedAtUtc = end,
        Type = BotDowntimeType.Deploy,
    };

    private static DateTime LocalToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Warsaw);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
