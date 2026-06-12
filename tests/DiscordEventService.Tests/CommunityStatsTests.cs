using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class CommunityStatsTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
        await _db.PresenceEvents.ExecuteDeleteAsync();
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
    public async Task Community_Week_CountsMessagesMemesAndActiveMembers()
    {
        var controller = new StatsController(_db);

        var dto = (await controller.Community("week", default)).Value!;

        Assert.Equal("week", dto.Range);
        // 3 messages this week (alice x2, bob x1); 1 prior-week message excluded from value.
        Assert.Equal(3, dto.Metrics.Messages.Value);
        // Memes = media posts (attachment OR embed): 2 this week.
        Assert.Equal(2, dto.Metrics.Memes.Value);
        // Active members = distinct authors this week = 2.
        Assert.Equal(2, dto.Metrics.ActiveMembers.Value);
        // Reactions received (event_type=0) this week = 2.
        Assert.Equal(2, dto.Metrics.ReactionsReceived.Value);

        // Spark is dense and fixed length (7 for week).
        Assert.Equal(7, dto.Metrics.Messages.Spark.Count);
        Assert.Equal(3, dto.Metrics.Messages.Spark.Sum());

        // prev window populated for week.
        Assert.NotNull(dto.Metrics.Messages.Prev);
        Assert.Equal(1, dto.Metrics.Messages.Prev);

        // Leaderboards: top chatter is alice with 2.
        Assert.Equal(Alice, dto.Leaderboards.TopChatters[0].UserDiscordId);
        Assert.Equal(2, dto.Leaderboards.TopChatters[0].Value);
    }

    [Fact]
    public async Task Community_All_HasNullPrevAndDenseSpark()
    {
        var controller = new StatsController(_db);

        var dto = (await controller.Community("all", default)).Value!;

        Assert.Equal("all", dto.Range);
        Assert.Null(dto.Metrics.Messages.Prev);
        // All-time messages = 4 (3 this week + 1 prior).
        Assert.Equal(4, dto.Metrics.Messages.Value);
        Assert.NotEmpty(dto.Metrics.Messages.Spark);
        Assert.Equal(4, dto.Metrics.Messages.Spark.Sum());
    }

    [Fact]
    public async Task Community_RejectsInvalidRange()
    {
        var controller = new StatsController(_db);

        var result = await controller.Community("decade", default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
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

        var thisWeek = DateTime.UtcNow.AddDays(-1);
        var priorWeek = DateTime.UtcNow.AddDays(-10); // inside prev 7-14d window

        _db.Messages.AddRange(
            Message(alice.Id, channel.Id, guild.Id, 1UL, thisWeek, attach: true),
            Message(alice.Id, channel.Id, guild.Id, 2UL, thisWeek, embed: true),
            Message(bob.Id, channel.Id, guild.Id, 3UL, thisWeek),
            // Prior-week message (counts in prev, excluded from current value).
            Message(alice.Id, channel.Id, guild.Id, 4UL, priorWeek));

        _db.ReactionEvents.AddRange(
            Reaction(Bob, "👍", 1UL, thisWeek),
            Reaction(Bob, "🎉", 2UL, thisWeek));

        var v = thisWeek;
        _db.VoiceStateEvents.AddRange(
            Voice(Alice, null, ChannelSf, VoiceEventType.Joined, v),
            Voice(Alice, ChannelSf, null, VoiceEventType.Left, v.AddMinutes(12)));

        _db.RawEventLogs.Add(new RawEventLogEntity
        {
            EventType = "MessageCreated",
            GuildDiscordId = GuildSf,
            EventJson = "{}",
            JsonSizeBytes = 2,
            ReceivedAtUtc = priorWeek,
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static MessageEntity Message(
        Guid authorId, Guid channelId, Guid guildId, ulong discordId, DateTime at,
        bool attach = false, bool embed = false) => new MessageEntity
        {
            DiscordId = discordId,
            AuthorId = authorId,
            ChannelId = channelId,
            GuildId = guildId,
            Content = "hi",
            HasAttachments = attach,
            HasEmbeds = embed,
            CreatedAtUtc = at,
        };

    private static ReactionEventEntity Reaction(ulong user, string emote, ulong messageId, DateTime at) => new ReactionEventEntity
    {
        UserDiscordId = user,
        GuildDiscordId = GuildSf,
        ChannelDiscordId = ChannelSf,
        MessageDiscordId = messageId,
        EmoteName = emote,
        EventType = ReactionEventType.Added,
        ReceivedAtUtc = at,
        EventTimestampUtc = at,
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
