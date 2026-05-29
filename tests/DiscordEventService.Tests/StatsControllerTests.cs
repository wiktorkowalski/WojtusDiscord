using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
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
        await _db.ReactionEvents.ExecuteDeleteAsync();
        await _db.VoiceStateEvents.ExecuteDeleteAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task TopMessages_RanksAuthorsByCount()
    {
        var controller = new StatsController(_db);
        var top = await controller.TopMessages(default);

        Assert.Equal("alice", top[0].Username);
        Assert.Equal(2, top[0].Count);
        Assert.Equal("bob", top[1].Username);
        Assert.Equal(1, top[1].Count);
    }

    [Fact]
    public async Task VoiceLeaderboard_SumsSessionMinutes()
    {
        var controller = new StatsController(_db);
        var voice = await controller.VoiceLeaderboard(default);

        var alice = Assert.Single(voice);
        Assert.Equal(Alice, alice.UserDiscordId);
        Assert.Equal(10, alice.Minutes); // 12:00 join -> 12:10 leave
    }

    [Fact]
    public async Task TopEmojis_CountsAndFlagsCustom()
    {
        var controller = new StatsController(_db);
        var emojis = await controller.TopEmojis(default);

        var thumbs = emojis.Single(e => e.EmoteName == "👍");
        Assert.Equal(2, thumbs.Count);
        Assert.False(thumbs.IsCustom);

        var custom = emojis.Single(e => e.EmoteName == "megalul");
        Assert.True(custom.IsCustom);
    }

    [Fact]
    public async Task ChannelActivity_CountsMessagesAndReactions()
    {
        var controller = new StatsController(_db);
        var channels = await controller.ChannelActivity(default);

        var general = Assert.Single(channels);
        Assert.Equal("general", general.ChannelName);
        Assert.Equal(3, general.MessageCount);
        Assert.Equal(3, general.ReactionCount);
    }

    [Fact]
    public async Task Overview_AggregatesTotals()
    {
        var controller = new StatsController(_db);

        var o = await controller.Overview(default);

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
    public async Task VolumeByType_RanksEventTypes()
    {
        var controller = new StatsController(_db);
        var volume = await controller.VolumeByType(default);

        Assert.Equal("MessageCreated", volume[0].EventType);
        Assert.Equal(2, volume[0].Count);
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

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static MessageEntity Message(Guid authorId, Guid channelId, Guid guildId, ulong discordId) => new()
    {
        DiscordId = discordId,
        AuthorId = authorId,
        ChannelId = channelId,
        GuildId = guildId,
        Content = "hi",
        CreatedAtUtc = new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc),
    };

    private static VoiceStateEventEntity Voice(
        ulong user, ulong guild, ulong? before, ulong? after, VoiceEventType type, DateTime at) => new()
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
        ulong user, ulong guild, ulong channel, string emote, ulong? emoteId) => new()
        {
            UserDiscordId = user,
            GuildDiscordId = guild,
            ChannelDiscordId = channel,
            MessageDiscordId = 1UL,
            EmoteName = emote,
            EmoteDiscordId = emoteId,
            EventType = ReactionEventType.Added,
            ReceivedAtUtc = DateTime.UtcNow,
            EventTimestampUtc = DateTime.UtcNow,
        };

    private static RawEventLogEntity Raw(string type, DateTime at) => new()
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
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
