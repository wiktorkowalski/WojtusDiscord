using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class MemeSampleServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong MemeChannelDiscordId = 10UL;
    private const ulong OtherChannelDiscordId = 20UL;

    private DiscordDbContext _db = null!;
    private GuildEntity _guild = null!;
    private ChannelEntity _memeChannel = null!;
    private ChannelEntity _otherChannel = null!;
    private UserEntity _author = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();

        _guild = new GuildEntity { DiscordId = 1UL, Name = "g" };
        _db.Guilds.Add(_guild);
        await _db.SaveChangesAsync();

        _memeChannel = new ChannelEntity { DiscordId = MemeChannelDiscordId, GuildId = _guild.Id, Name = "memes", Type = ChannelType.Text };
        _otherChannel = new ChannelEntity { DiscordId = OtherChannelDiscordId, GuildId = _guild.Id, Name = "general", Type = ChannelType.Text };
        _author = new UserEntity { DiscordId = 5UL, Username = "u" };
        _db.AddRange(_memeChannel, _otherChannel, _author);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task SampleAsync_FiltersToImageAttachmentsInMemeChannelsOnly()
    {
        AddMessage(_memeChannel, 100UL, Year(2023), Attachment(1000UL, "a.jpg"));
        AddMessage(_memeChannel, 101UL, Year(2023), Attachment(1001UL, "clip.mp4"));
        AddMessage(_otherChannel, 102UL, Year(2023), Attachment(1002UL, "b.png"));
        AddMessage(_memeChannel, 103UL, Year(2023), Attachment(1003UL, "c.png"), isDeleted: true);
        await _db.SaveChangesAsync();

        var sample = await NewService().SampleAsync(50, CancellationToken.None);

        var item = Assert.Single(sample);
        Assert.Equal(100UL, item.MessageDiscordId);
        Assert.Equal(1000UL, item.AttachmentDiscordId);
        Assert.Equal(MemeChannelDiscordId, item.ChannelDiscordId);
        Assert.Equal(1UL, item.GuildDiscordId);
    }

    [Fact]
    public async Task SampleAsync_StratifiesAcrossYears()
    {
        var nextDiscordId = 200UL;
        var nextAttachmentId = 2000UL;
        foreach (var year in new[] { 2018, 2020, 2022, 2024 })
        {
            for (var i = 0; i < 5; i++)
            {
                AddMessage(_memeChannel, nextDiscordId++, Year(year), Attachment(nextAttachmentId++, $"m{year}-{i}.jpg"));
            }
        }
        await _db.SaveChangesAsync();

        var sample = await NewService().SampleAsync(8, CancellationToken.None);

        Assert.Equal(8, sample.Count);
        var perYear = sample.GroupBy(s => s.CreatedAtUtc.Year).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new[] { 2018, 2020, 2022, 2024 }, perYear.Keys.Order());
        Assert.All(perYear.Values, count => Assert.Equal(2, count));
    }

    [Fact]
    public async Task SampleAsync_ExpandsMultiAttachmentMessagesPerAttachment()
    {
        AddMessage(_memeChannel, 300UL, Year(2021),
            $"[{Attachment(3000UL, "one.png")},{Attachment(3001UL, "two.webp")},{Attachment(3002UL, "notes.txt")}]",
            wrap: false);
        await _db.SaveChangesAsync();

        var sample = await NewService().SampleAsync(50, CancellationToken.None);

        Assert.Equal(2, sample.Count);
        Assert.All(sample, s => Assert.Equal(300UL, s.MessageDiscordId));
        Assert.Equal(new[] { 3000UL, 3001UL }, sample.Select(s => s.AttachmentDiscordId).Order().ToArray());
    }

    private MemeSampleService NewService() =>
        new(_db,
            Options.Create(new MemeIndexOptions { ChannelIds = [MemeChannelDiscordId] }),
            NullLogger<MemeSampleService>.Instance);

    private void AddMessage(ChannelEntity channel, ulong discordId, DateTime createdAtUtc, string attachments,
        bool isDeleted = false, bool wrap = true)
    {
        _db.Messages.Add(new MessageEntity
        {
            DiscordId = discordId,
            ChannelId = channel.Id,
            GuildId = _guild.Id,
            AuthorId = _author.Id,
            HasAttachments = true,
            AttachmentsJson = wrap ? $"[{attachments}]" : attachments,
            CreatedAtUtc = createdAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? createdAtUtc : null
        });
    }

    // Matches the 4-field PascalCase shape MessageEventHandler serializes.
    private static string Attachment(ulong id, string fileName) =>
        $"{{\"Id\":{id},\"Url\":\"https://cdn.discordapp.com/x/{id}/{fileName}?ex=0\",\"FileName\":\"{fileName}\",\"FileSize\":123}}";

    private static DateTime Year(int year) => new DateTime(year, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
