using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class RawEventsControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
    public async Task GetTypes_MixedEventTypes_ReturnsCountsPerType()
    {
        await SeedAsync(("MessageCreated", false), ("MessageCreated", false), ("PresenceUpdated", false));
        var controller = new RawEventsController(_db);

        var types = Ok<IReadOnlyList<RawEventTypeDto>>(await controller.GetTypes(default));

        Assert.Equal(2, types.Single(t => t.EventType == "MessageCreated").Count);
        Assert.Equal(1, types.Single(t => t.EventType == "PresenceUpdated").Count);
    }

    [Fact]
    public async Task GetEvents_TypeAndFailedFilters_ReturnsOnlyMatching()
    {
        await SeedAsync(("MessageCreated", false), ("MessageCreated", true), ("PresenceUpdated", false));
        var controller = new RawEventsController(_db);

        var byType = Ok<PagedResult<RawEventSummaryDto>>(
            await controller.GetEvents(eventType: "MessageCreated", ct: default));
        Assert.Equal(2, byType.TotalCount);

        var failed = Ok<PagedResult<RawEventSummaryDto>>(
            await controller.GetEvents(failedOnly: true, ct: default));
        Assert.Equal(1, failed.TotalCount);
        Assert.True(failed.Items[0].SerializationFailed);
    }

    [Fact]
    public async Task GetEvents_WithUnspecifiedKindSince_FiltersWithoutThrowing()
    {
        // Seeded at 12:00, 12:01, 12:02 (UTC).
        await SeedAsync(("A", false), ("B", false), ("C", false));
        var controller = new RawEventsController(_db);

        // Kind=Unspecified, as query-string binding produces — must not throw against timestamptz.
        var since = new DateTime(2026, 5, 1, 12, 1, 0, DateTimeKind.Unspecified);
        var page = Ok<PagedResult<RawEventSummaryDto>>(await controller.GetEvents(since: since, ct: default));

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetEvent_ReturnsPayload_OrNotFound()
    {
        await SeedAsync(("MessageCreated", false));
        var controller = new RawEventsController(_db);
        var id = await _db.RawEventLogs.Select(r => r.Id).FirstAsync();

        var detail = Ok<RawEventDetailDto>(await controller.GetEvent(id, default));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, detail.Payload.ValueKind);

        var missing = await controller.GetEvent(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(missing.Result);
    }

    private async Task SeedAsync(params (string EventType, bool Failed)[] events)
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var i = 0;
        foreach (var (eventType, failed) in events)
        {
            _db.RawEventLogs.Add(new RawEventLogEntity
            {
                EventType = eventType,
                GuildDiscordId = 742554855180206203UL,
                EventJson = "{\"sample\":true}",
                JsonSizeBytes = 16,
                IsSerializationFailed = failed,
                ReceivedAtUtc = t.AddMinutes(i++),
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static T Ok<T>(ActionResult<T> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<T>(ok.Value);
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
