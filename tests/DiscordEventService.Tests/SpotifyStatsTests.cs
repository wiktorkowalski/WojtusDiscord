using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class SpotifyStatsTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong GuildSf = 742554855180206203UL;
    private const ulong Alice = 100UL;

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Activities.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Spotify_NowPlaying_ReturnsActiveListeningWithFirstArtist()
    {
        var controller = new StatsController(_db);

        var dto = await controller.Spotify(default);

        var np = Assert.Single(dto.NowPlaying);
        Assert.Equal(Alice, np.UserDiscordId);
        Assert.Equal("alice", np.Username);
        Assert.Equal("Song A", np.Track);
        Assert.Equal("Artist1", np.Artist); // first element of the JSON array
        Assert.Equal("Album A", np.Album);
        Assert.Equal("http://art/a.png", np.AlbumArtUrl);
    }

    [Fact]
    public async Task Spotify_TopTracks_RanksByPlayCount()
    {
        var controller = new StatsController(_db);

        var dto = await controller.Spotify(default);

        Assert.Equal("Song A", dto.TopTracks[0].Track); // played 3x
        Assert.Equal(3, dto.TopTracks[0].Plays);
        Assert.Equal("Artist1", dto.TopTracks[0].Artist);
        Assert.Equal("Song B", dto.TopTracks[1].Track); // played 1x
        Assert.Equal(1, dto.TopTracks[1].Plays);
    }

    private async Task SeedAsync()
    {
        var guild = new GuildEntity { DiscordId = GuildSf, Name = "G" };
        var alice = new UserEntity { DiscordId = Alice, Username = "alice" };
        _db.Guilds.Add(guild);
        _db.Users.Add(alice);
        await _db.SaveChangesAsync();

        var t = DateTime.UtcNow;
        // Active "now playing" Song A (Listening = activity_type 2).
        _db.Activities.Add(Activity(alice.Id, guild.Id, "Song A", "Album A", "http://art/a.png",
            """["Artist1","Artist2"]""", isActive: true, lastSeen: t));
        // Two more inactive Song A plays (history) + one Song B play.
        _db.Activities.Add(Activity(alice.Id, guild.Id, "Song A", "Album A", "http://art/a.png",
            """["Artist1"]""", isActive: false, lastSeen: t.AddMinutes(-10), ended: t.AddMinutes(-9)));
        _db.Activities.Add(Activity(alice.Id, guild.Id, "Song A", "Album A", "http://art/a.png",
            """["Artist1"]""", isActive: false, lastSeen: t.AddMinutes(-20), ended: t.AddMinutes(-19)));
        _db.Activities.Add(Activity(alice.Id, guild.Id, "Song B", "Album B", "http://art/b.png",
            """["Artist3"]""", isActive: false, lastSeen: t.AddMinutes(-30), ended: t.AddMinutes(-29)));
        // A non-Spotify activity (Playing) must be ignored.
        _db.Activities.Add(new ActivityEntity
        {
            UserId = alice.Id,
            GuildId = guild.Id,
            ActivityType = 0,
            Name = "Some Game",
            IsActive = true,
            LastSeenAtUtc = t,
        });

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static ActivityEntity Activity(
        Guid userId, Guid guildId, string track, string album, string art, string artistsJson,
        bool isActive, DateTime lastSeen, DateTime? ended = null) => new()
        {
            UserId = userId,
            GuildId = guildId,
            ActivityType = 2, // Listening
            SpotifySongTitle = track,
            SpotifyAlbumTitle = album,
            SpotifyAlbumArtUrl = art,
            SpotifyArtistsJson = artistsJson,
            IsActive = isActive,
            LastSeenAtUtc = lastSeen,
            EndedAtUtc = isActive ? null : ended ?? lastSeen,
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
