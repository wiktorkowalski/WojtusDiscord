using System.Text.Json;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class EntitiesControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong UserSnowflake = 111111111111111111UL;
    private const ulong ChannelSnowflake = 222222222222222222UL;
    private const ulong MessageSnowflake = 333333333333333333UL;

    private DiscordDbContext _db = null!;
    private Guid _channelId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.MessageEditHistory.ExecuteDeleteAsync();
        await _db.Messages.ExecuteDeleteAsync();
        await _db.Members.ExecuteDeleteAsync();
        await _db.Channels.ExecuteDeleteAsync();
        await _db.Users.ExecuteDeleteAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task GetMessages_ResolvesAuthorAndChannelNames_WithStringSnowflakes()
    {
        var controller = new EntitiesController(_db);

        var page = (await controller.GetMessages(ct: default)).Value!;

        var msg = Assert.Single(page.Items);
        Assert.Equal("alice", msg.AuthorName);
        Assert.Equal("general", msg.ChannelName);
        Assert.Equal(UserSnowflake, msg.AuthorDiscordId);

        var json = JsonSerializer.Serialize(msg, DashboardJson.CreateOptions());
        Assert.Contains($"\"authorDiscordId\":\"{UserSnowflake}\"", json);
        Assert.Contains($"\"channelDiscordId\":\"{ChannelSnowflake}\"", json);
    }

    [Fact]
    public async Task GetMessages_FiltersByChannel()
    {
        var controller = new EntitiesController(_db);

        var matching = (await controller.GetMessages(channelId: _channelId, ct: default)).Value!;
        Assert.Equal(1, matching.TotalCount);

        var other = (await controller.GetMessages(channelId: Guid.NewGuid(), ct: default)).Value!;
        Assert.Equal(0, other.TotalCount);
    }

    [Fact]
    public async Task GetUser_ReturnsDetailWithMembershipCount_OrNotFound()
    {
        var controller = new EntitiesController(_db);
        var userId = await _db.Users.Select(u => u.Id).FirstAsync();

        var ok = Assert.IsType<OkObjectResult>((await controller.GetUser(userId, default)).Result);
        var detail = Assert.IsType<UserDetailDto>(ok.Value);
        Assert.Equal("alice", detail.Username);
        Assert.Equal(1, detail.MembershipCount);

        Assert.IsType<NotFoundResult>((await controller.GetUser(Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task GetChannel_ReturnsMessageCount_AndDecodedType()
    {
        var controller = new EntitiesController(_db);

        var ok = Assert.IsType<OkObjectResult>((await controller.GetChannel(_channelId, default)).Result);
        var detail = Assert.IsType<ChannelDetailDto>(ok.Value);
        Assert.Equal("Text", detail.Type);
        Assert.Equal(1, detail.MessageCount);
    }

    private async Task SeedAsync()
    {
        var guild = new GuildEntity { DiscordId = 742554855180206203UL, Name = "Guild" };
        var user = new UserEntity { DiscordId = UserSnowflake, Username = "alice", GlobalName = "Alice" };
        _db.Guilds.Add(guild);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var channel = new ChannelEntity
        {
            DiscordId = ChannelSnowflake,
            GuildId = guild.Id,
            Name = "general",
            Type = ChannelType.Text,
        };
        _db.Channels.Add(channel);
        _db.Members.Add(new MemberEntity { UserId = user.Id, GuildId = guild.Id, Nickname = "Al" });
        await _db.SaveChangesAsync();
        _channelId = channel.Id;

        _db.Messages.Add(new MessageEntity
        {
            DiscordId = MessageSnowflake,
            ChannelId = channel.Id,
            GuildId = guild.Id,
            AuthorId = user.Id,
            Content = "hello world",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
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
