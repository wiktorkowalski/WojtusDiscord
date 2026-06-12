using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class FailedEventResolveTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.FailedEvents.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ResolveAsync_SetsResolutionFields_AndClearsHealthCheckCount()
    {
        var id = await SeedFailedEventAsync(DateTime.UtcNow);
        var service = NewService();

        // HealthCheckJob alert counts unresolved rows in a recent window — 1 before resolution.
        Assert.Equal(1, await HealthCheckUnresolvedCountAsync());

        var resolved = await service.ResolveAsync(id, "transient blip, acked");

        Assert.True(resolved);
        await using var verify = NewContext();
        var row = await verify.FailedEvents.SingleAsync(f => f.Id == id);
        Assert.True(row.IsResolved);
        Assert.NotNull(row.ResolvedAtUtc);
        Assert.Equal("transient blip, acked", row.ResolutionNotes);

        // The discriminating proof: the alert query now excludes it (clears by resolution).
        Assert.Equal(0, await HealthCheckUnresolvedCountAsync());
    }

    [Fact]
    public async Task ResolveAsync_WhenAlreadyResolved_IsNoOpReturningFalse()
    {
        var id = await SeedFailedEventAsync(DateTime.UtcNow);
        var service = NewService();

        Assert.True(await service.ResolveAsync(id, "first"));
        Assert.False(await service.ResolveAsync(id, "second"));

        await using var verify = NewContext();
        var row = await verify.FailedEvents.SingleAsync(f => f.Id == id);
        Assert.Equal("first", row.ResolutionNotes); // second call did not overwrite
    }

    [Fact]
    public async Task ResolveAsync_WhenIdUnknown_ReturnsFalse()
    {
        var service = NewService();
        Assert.False(await service.ResolveAsync(Guid.NewGuid(), "nope"));
    }

    // Mirrors HealthCheckJob.CheckFailedEventsAsync's count query.
    private async Task<int> HealthCheckUnresolvedCountAsync()
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-60);
        await using var verify = NewContext();
        return await verify.FailedEvents.CountAsync(f => f.FailedAtUtc > windowStart && !f.IsResolved);
    }

    private async Task<Guid> SeedFailedEventAsync(DateTime failedAtUtc)
    {
        var entity = new FailedEventEntity
        {
            EventType = "GuildMemberUpdated",
            HandlerName = "MemberEventHandler",
            ExceptionType = "System.NullReferenceException",
            ExceptionMessage = "boom",
            FailedAtUtc = failedAtUtc,
            EventReceivedAtUtc = failedAtUtc
        };
        _db.FailedEvents.Add(entity);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return entity.Id;
    }

    private FailedEventService NewService() =>
        new FailedEventService(NewContext(), NullLogger<FailedEventService>.Instance, new FakeHostEnvironment());

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    // ResolveAsync never touches the environment (only RecordFailureAsync's JSONL fallback does).
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "DiscordEventService.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
