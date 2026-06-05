using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class ProfileTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
        await _db.UserNameHistory.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Profile_AggregatesAllTimeStats()
    {
        var controller = new PeopleController(_db);

        var dto = (await controller.Profile(unchecked((long)Alice), default)).Value!;

        Assert.Equal(Alice, dto.UserDiscordId);
        Assert.Equal("alice", dto.Username);
        Assert.Equal(3, dto.MessageCount);            // alice authored 3
        Assert.Equal(1, dto.MemeCount);               // one with attachment
        Assert.Equal(2, dto.ReactionsReceivedCount);  // 2 reactions on alice's msgs
        Assert.Equal(10, dto.VoiceMinutes);           // one 10-min session
        Assert.Equal(5, dto.OnlineMinutes);           // only the 5-min segment; 40-min gap dropped by 30-min cap
        Assert.Equal("online", dto.Status);           // latest presence desktop=Online(1)

        Assert.NotNull(dto.FavoriteEmote);
        Assert.Equal("👍", dto.FavoriteEmote!.EmoteName); // alice gave 👍 x2, 🎉 x1

        Assert.NotNull(dto.BusiestChannel);
        Assert.Equal("general", dto.BusiestChannel!.ChannelName);

        Assert.Equal(14, dto.MessagesDaily14.Count);  // dense 14-day series

        var change = Assert.Single(dto.NameHistory);
        Assert.Equal("alice_old", change.UsernameBefore);
    }

    [Fact]
    public async Task Profile_UnknownSnowflake_Returns404()
    {
        var controller = new PeopleController(_db);

        var result = await controller.Profile(999999L, default);

        Assert.IsType<NotFoundResult>(result.Result);
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

        var t = DateTime.UtcNow.AddDays(-1);
        _db.Messages.AddRange(
            Message(alice.Id, channel.Id, guild.Id, 1UL, t, attach: true),
            Message(alice.Id, channel.Id, guild.Id, 2UL, t),
            Message(alice.Id, channel.Id, guild.Id, 3UL, t),
            Message(bob.Id, channel.Id, guild.Id, 4UL, t));

        // Reactions received on alice's messages (1, 2).
        _db.ReactionEvents.AddRange(
            Reaction(Bob, "🔥", 1UL, t),
            Reaction(Bob, "🔥", 2UL, t),
            // Reactions GIVEN by alice (for favouriteEmote): 👍 x2, 🎉 x1.
            Reaction(Alice, "👍", 4UL, t),
            Reaction(Alice, "👍", 4UL, t),
            Reaction(Alice, "🎉", 4UL, t));

        // Voice: alice 10-minute session.
        _db.VoiceStateEvents.AddRange(
            Voice(Alice, null, ChannelSf, VoiceEventType.Joined, t),
            Voice(Alice, ChannelSf, null, VoiceEventType.Left, t.AddMinutes(10)));

        // Presence sessionization (desktop = DiscordUserStatus int: Idle=2, Online=1):
        //   e1@t      idle, gap 5min  -> counts 5 online minutes (<= 30 cap)
        //   e2@t+5    online, gap 40min -> dropped (> 30 cap proves filter, not clamp)
        //   e3@t+45   online, no follower -> excluded; also the latest -> status "online"
        _db.PresenceEvents.AddRange(
            Presence(Alice, desktop: 2, at: t),
            Presence(Alice, desktop: 1, at: t.AddMinutes(5)),
            Presence(Alice, desktop: 1, at: t.AddMinutes(45)));

        _db.UserNameHistory.Add(new UserNameHistoryEntity
        {
            UserId = alice.Id,
            UsernameBefore = "alice_old",
            UsernameAfter = "alice",
            ChangedAtUtc = t,
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static MessageEntity Message(
        Guid authorId, Guid channelId, Guid guildId, ulong discordId, DateTime at, bool attach = false) => new()
        {
            DiscordId = discordId,
            AuthorId = authorId,
            ChannelId = channelId,
            GuildId = guildId,
            Content = "hi",
            HasAttachments = attach,
            CreatedAtUtc = at,
        };

    private static ReactionEventEntity Reaction(ulong user, string emote, ulong messageId, DateTime at) => new()
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
        ulong user, ulong? before, ulong? after, VoiceEventType type, DateTime at) => new()
        {
            UserDiscordId = user,
            GuildDiscordId = GuildSf,
            ChannelDiscordIdBefore = before,
            ChannelDiscordIdAfter = after,
            EventType = type,
            ReceivedAtUtc = at,
            EventTimestampUtc = at,
        };

    private static PresenceEventEntity Presence(ulong user, DateTime at, int desktop = 0) => new()
    {
        UserDiscordId = user,
        GuildDiscordId = GuildSf,
        EventType = PresenceEventType.Updated,
        DesktopStatusAfter = desktop,
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
