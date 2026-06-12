using System.Text.Json;
using DiscordEventService.Controllers;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class TablesControllerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Larger than 2^53, so it round-trips correctly ONLY if serialized as a string.
    private const ulong GuildSnowflake = 742554855180206203UL;

    private DiscordDbContext _db = null!;
    private SchemaCatalog _catalog = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.RawEventLogs.ExecuteDeleteAsync();
        _catalog = SchemaCatalog.Build(_db.Model);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task GetTables_ListsEntityTables_AndExcludesMigrationsHistory()
    {
        var controller = NewController();

        var tables = Ok<IReadOnlyList<TableInfoDto>>(await controller.GetTables(default));

        Assert.Contains(tables, t => t.Name == "raw_event_logs");
        Assert.Contains(tables, t => t.Name == "messages");
        Assert.DoesNotContain(tables, t => t.Name == "__EFMigrationsHistory");
    }

    [Fact]
    public void GetColumns_ReportsSnowflakeJsonAndEnumKinds()
    {
        var controller = NewController();

        var rawCols = Ok<IReadOnlyList<ColumnMetadataDto>>(controller.GetColumns("raw_event_logs"));
        Assert.Equal("snowflake", rawCols.Single(c => c.Name == "guild_discord_id").Kind);
        Assert.Equal("json", rawCols.Single(c => c.Name == "event_json").Kind);
        Assert.Equal("string", rawCols.Single(c => c.Name == "event_type").Kind);

        // Enum columns expose their decoded values for client-side rendering.
        var channelCols = Ok<IReadOnlyList<ColumnMetadataDto>>(controller.GetColumns("channels"));
        var typeCol = channelCols.Single(c => c.Name == "type");
        Assert.Equal("enum", typeCol.Kind);
        Assert.NotNull(typeCol.EnumValues);
        Assert.Contains(typeCol.EnumValues!, e => e.Name == "Voice");
    }

    [Fact]
    public void GetColumns_UnknownTable_ReturnsBadRequest()
    {
        var controller = NewController();
        var result = controller.GetColumns("definitely_not_a_table");
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetRows_ReturnsSeededRows_WithExactTotal()
    {
        await SeedRawEventsAsync(("MessageCreated", GuildSnowflake), ("PresenceUpdated", 999UL));
        var controller = NewController();

        var paged = Ok<PagedResult<Dictionary<string, object?>>>(
            await controller.GetRows("raw_event_logs", ct: default));

        Assert.Equal(2, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
        Assert.All(paged.Items, row => Assert.True(row.ContainsKey("guild_discord_id")));
    }

    [Fact]
    public async Task GetRows_SerializesSnowflakeAsString_ThroughJsonPipeline()
    {
        await SeedRawEventsAsync(("MessageCreated", GuildSnowflake));
        var controller = NewController();

        var paged = Ok<PagedResult<Dictionary<string, object?>>>(
            await controller.GetRows("raw_event_logs", ct: default));

        // Serialize through the SAME options the API uses — a direct dict inspection
        // would never exercise the ulong->string converter this test guards.
        var json = JsonSerializer.Serialize(paged.Items[0], DashboardJson.CreateOptions());

        Assert.Contains($"\"guild_discord_id\":\"{GuildSnowflake}\"", json);
        // Must NOT appear as a bare JSON number (would lose precision in a browser).
        Assert.DoesNotContain($"\"guild_discord_id\":{GuildSnowflake}", json);
    }

    [Fact]
    public async Task GetRows_FiltersByColumn()
    {
        await SeedRawEventsAsync(("MessageCreated", 1UL), ("PresenceUpdated", 2UL), ("MessageDeleted", 3UL));
        var controller = NewController();

        var paged = Ok<PagedResult<Dictionary<string, object?>>>(
            await controller.GetRows("raw_event_logs", filterColumn: "event_type", filter: "Message", ct: default));

        Assert.Equal(2, paged.TotalCount);
    }

    [Fact]
    public async Task GetRows_RejectsUnknownTableSortAndFilterColumns()
    {
        var controller = NewController();

        Assert.IsType<BadRequestObjectResult>((await controller.GetRows("evil_table", ct: default)).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await controller.GetRows("raw_event_logs", sort: "1; DROP TABLE messages", ct: default)).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await controller.GetRows("raw_event_logs", filterColumn: "nope", filter: "x", ct: default)).Result);
    }

    private async Task SeedRawEventsAsync(params (string EventType, ulong GuildId)[] events)
    {
        foreach (var (eventType, guildId) in events)
        {
            _db.RawEventLogs.Add(new RawEventLogEntity
            {
                EventType = eventType,
                GuildDiscordId = guildId,
                EventJson = "{\"sample\":true}",
                JsonSizeBytes = 16,
                ReceivedAtUtc = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private TablesController NewController() => new TablesController(_db, _catalog);

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
