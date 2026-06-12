using DiscordEventService.Data;
using DiscordEventService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// A payload whose getter throws during serialization, forcing SerializeEvent's failure path.
file sealed class ThrowingPayload
{
    public string Ok => "fine";
    public string Boom => throw new InvalidOperationException("kaboom");
}

file sealed class GoodPayload
{
    public string Name { get; init; } = "ok";
    public int Count { get; init; } = 1;
}

public sealed class SerializeEventUnitTests
{
    [Fact]
    public void SerializeEvent_WhenSuccessful_ReturnsJsonWithNoError()
    {
        var result = NewService().SerializeEvent(new GoodPayload());

        Assert.False(result.Failed);
        Assert.Null(result.Error);
        Assert.Contains("\"name\":\"ok\"", result.Json);
    }

    [Fact]
    public void SerializeEvent_WhenSerializationThrows_ReturnsFlaggedStubAndError()
    {
        var result = NewService().SerializeEvent(new ThrowingPayload());

        Assert.True(result.Failed);
        Assert.NotNull(result.Error);
        // Stub keeps the marker + type so the error stays queryable, but is NOT the real payload.
        Assert.Contains(RawEventLogService.SerializationFailedMarker, result.Json);
        Assert.Contains(nameof(ThrowingPayload), result.Json);
    }

    // dbContext/logger are unused by SerializeEvent, so no DB needed here.
    private static RawEventLogService NewService()
        => new RawEventLogService(null!, NullLogger<RawEventLogService>.Instance);
}

public sealed class RawEventLogPersistenceTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task SerializeAndLogAsync_WhenSerializationFails_PersistsFlaggedStub()
    {
        var service = new RawEventLogService(_db, NullLogger<RawEventLogService>.Instance);

        var result = await service.SerializeAndLogAsync(new ThrowingPayload(), "ThrowingEvent");
        await _db.SaveChangesAsync();

        Assert.True(result.Failed);

        await using var verify = NewContext();
        var row = await verify.RawEventLogs.SingleAsync(r => r.EventType == "ThrowingEvent");
        Assert.True(row.IsSerializationFailed);
        Assert.Contains(RawEventLogService.SerializationFailedMarker, row.EventJson);
    }

    [Fact]
    public async Task SerializeAndLogAsync_WhenSerializationSucceeds_PersistsUnflaggedRow()
    {
        var service = new RawEventLogService(_db, NullLogger<RawEventLogService>.Instance);

        var result = await service.SerializeAndLogAsync(new GoodPayload(), "GoodEvent");
        await _db.SaveChangesAsync();

        Assert.False(result.Failed);

        await using var verify = NewContext();
        var row = await verify.RawEventLogs.SingleAsync(r => r.EventType == "GoodEvent");
        Assert.False(row.IsSerializationFailed);
        Assert.DoesNotContain(RawEventLogService.SerializationFailedMarker, row.EventJson);
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
