using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class TimelineControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
    public async Task GetTimeline_PagesThroughAllRows_NewestFirst_NoDupesOrGaps()
    {
        var baseTime = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        // 5 rows, two of which share a timestamp to exercise the id tiebreak.
        await SeedAsync(
            ("A", baseTime.AddMinutes(1), null),
            ("B", baseTime.AddMinutes(2), null),
            ("C", baseTime.AddMinutes(3), null),
            ("D", baseTime.AddMinutes(3), null),
            ("E", baseTime.AddMinutes(5), null));

        var controller = new TimelineController(_db);

        var collected = new List<TimelineEventDto>();
        string? cursor = null;
        var guard = 0;
        while (true)
        {
            var page = Ok(await controller.GetTimeline(pageSize: 2, cursor: cursor));
            collected.AddRange(page.Events);
            cursor = page.NextCursor;
            if (!page.HasMore) break;
            Assert.True(++guard < 20, "pagination did not terminate");
        }

        Assert.Equal(5, collected.Count);
        Assert.Equal(5, collected.Select(e => e.Id).Distinct().Count());

        // Strictly descending by (received_at, id) — the keyset order.
        for (var i = 1; i < collected.Count; i++)
        {
            var prev = collected[i - 1];
            var cur = collected[i];
            var ordered = prev.ReceivedAtUtc > cur.ReceivedAtUtc
                || (prev.ReceivedAtUtc == cur.ReceivedAtUtc && prev.Id.CompareTo(cur.Id) > 0);
            Assert.True(ordered, $"out of order at {i}");
        }
    }

    [Fact]
    public async Task GetTimeline_FiltersByEventTypeAndUser()
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(
            ("MessageCreated", t.AddMinutes(1), 10UL),
            ("PresenceUpdated", t.AddMinutes(2), 20UL),
            ("MessageCreated", t.AddMinutes(3), 20UL));

        var controller = new TimelineController(_db);

        var byType = Ok(await controller.GetTimeline(eventType: "MessageCreated"));
        Assert.Equal(2, byType.Events.Count);
        Assert.All(byType.Events, e => Assert.Equal("MessageCreated", e.EventType));

        var byUser = Ok(await controller.GetTimeline(userId: 20UL));
        Assert.Equal(2, byUser.Events.Count);
        Assert.All(byUser.Events, e => Assert.Equal(20UL, e.UserDiscordId));
    }

    [Fact]
    public async Task GetTimeline_PayloadIsStructuredJson()
    {
        await SeedAsync(("MessageCreated", DateTime.UtcNow, null));
        var controller = new TimelineController(_db);

        var page = Ok(await controller.GetTimeline());

        Assert.Single(page.Events);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, page.Events[0].Payload.ValueKind);
        Assert.True(page.Events[0].Payload.TryGetProperty("sample", out _));
    }

    private async Task SeedAsync(params (string EventType, DateTime ReceivedAt, ulong? UserId)[] events)
    {
        foreach (var (eventType, receivedAt, userId) in events)
        {
            _db.RawEventLogs.Add(new RawEventLogEntity
            {
                EventType = eventType,
                GuildDiscordId = 742554855180206203UL,
                UserDiscordId = userId,
                EventJson = "{\"sample\":true}",
                JsonSizeBytes = 16,
                ReceivedAtUtc = receivedAt,
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static TimelinePage Ok(ActionResult<TimelinePage> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TimelinePage>(ok.Value);
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
