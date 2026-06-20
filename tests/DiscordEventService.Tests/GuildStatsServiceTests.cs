using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

// The typed top_posters leaderboard (#238 §4) against a real Postgres: correct ranking, optional
// channel and time-window filters, limit clamping, and a clean empty result for an unknown guild.
public sealed class GuildStatsServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildId = 7777UL;

    private DiscordDbContext _db = null!;
    private Guid _guildKey;
    private Guid _generalId;
    private Guid _memesId;
    private Guid _alice;
    private Guid _bob;
    private Guid _carol;
    private ulong _nextMessageId = 5000UL;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        var guild = new GuildEntity { DiscordId = GuildId, Name = "g" };
        _db.Guilds.Add(guild);
        await _db.SaveChangesAsync();
        _guildKey = guild.Id;

        _generalId = await AddChannelAsync(1UL, "general");
        _memesId = await AddChannelAsync(2UL, "memes");
        _alice = await AddUserAsync(10UL, "alice");
        _bob = await AddUserAsync(11UL, "bob");
        _carol = await AddUserAsync(12UL, "carol");

        var now = DateTime.UtcNow;
        await AddMessagesAsync(_alice, _generalId, 3, now);
        await AddMessagesAsync(_alice, _memesId, 2, now);
        await AddMessagesAsync(_bob, _generalId, 3, now);
        await AddMessagesAsync(_carol, _memesId, 1, now);
        // An old bob message that a recent-window filter must exclude.
        await AddMessagesAsync(_bob, _generalId, 1, now.AddDays(-30));
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task TopPostersAsync_RanksByMessageCountDescending()
    {
        var posters = await NewService().TopPostersAsync(GuildId, limit: 10, sinceDays: 0, channelNameContains: null, CancellationToken.None);

        Assert.Equal(new[] { "alice", "bob", "carol" }, posters.Select(p => p.Username).ToArray());
        Assert.Equal(new long[] { 5, 4, 1 }, posters.Select(p => p.MessageCount).ToArray());
        Assert.Equal(10UL, posters[0].UserDiscordId);
    }

    [Fact]
    public async Task TopPostersAsync_ChannelFilter_CountsOnlyMatchingChannels()
    {
        var posters = await NewService().TopPostersAsync(GuildId, limit: 10, sinceDays: 0, channelNameContains: "meme", CancellationToken.None);

        // Only #memes: alice 2, carol 1, bob absent.
        Assert.Equal(new[] { "alice", "carol" }, posters.Select(p => p.Username).ToArray());
        Assert.Equal(new long[] { 2, 1 }, posters.Select(p => p.MessageCount).ToArray());
    }

    [Fact]
    public async Task TopPostersAsync_SinceDays_ExcludesOlderMessages()
    {
        var posters = await NewService().TopPostersAsync(GuildId, limit: 10, sinceDays: 7, channelNameContains: null, CancellationToken.None);

        // bob's 30-day-old message drops out, so bob = 3, alice still 5.
        Assert.Equal(5, posters.Single(p => p.Username == "alice").MessageCount);
        Assert.Equal(3, posters.Single(p => p.Username == "bob").MessageCount);
    }

    [Fact]
    public async Task TopPostersAsync_LimitIsRespectedAndClamped()
    {
        var one = await NewService().TopPostersAsync(GuildId, limit: 1, sinceDays: 0, channelNameContains: null, CancellationToken.None);
        Assert.Single(one);
        Assert.Equal("alice", one[0].Username);

        // Over-large limit is clamped to MaxLimit, not an error; here it just returns all 3.
        var all = await NewService().TopPostersAsync(GuildId, limit: 1000, sinceDays: 0, channelNameContains: null, CancellationToken.None);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task TopPostersAsync_UnknownGuild_ReturnsEmpty()
    {
        var posters = await NewService().TopPostersAsync(123UL, limit: 10, sinceDays: 0, channelNameContains: null, CancellationToken.None);
        Assert.Empty(posters);
    }

    private GuildStatsService NewService() => new(NewContext());

    private async Task<Guid> AddChannelAsync(ulong discordId, string name)
    {
        var channel = new ChannelEntity { DiscordId = discordId, GuildId = _guildKey, Name = name, Type = ChannelType.Text };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();
        return channel.Id;
    }

    private async Task<Guid> AddUserAsync(ulong discordId, string username)
    {
        var user = new UserEntity { DiscordId = discordId, Username = username };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task AddMessagesAsync(Guid authorId, Guid channelId, int count, DateTime createdAtUtc)
    {
        for (var i = 0; i < count; i++)
        {
            _db.Messages.Add(new MessageEntity
            {
                DiscordId = _nextMessageId++,
                ChannelId = channelId,
                GuildId = _guildKey,
                AuthorId = authorId,
                CreatedAtUtc = createdAtUtc,
            });
        }
        await _db.SaveChangesAsync();
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
