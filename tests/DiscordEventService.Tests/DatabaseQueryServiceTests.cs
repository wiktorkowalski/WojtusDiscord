using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The load-bearing §4 contract against a REAL Postgres with the REAL non-superuser query role
// (provisioned exactly as dev/prod do). The security crux: even though EF's login is the superuser,
// query_database drops to a SELECT-only role via SET LOCAL ROLE, so a privileged file function is
// rejected and a write — including one hidden in a data-modifying CTE — is rejected; snowflakes are
// stringified, results are row-capped, slow queries are cut off, and EF's connection is left intact.
public sealed class DatabaseQueryServiceTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong SeedGuildId = 9999UL;
    // The role the AddConversationQueryRole migration provisions (it runs as superuser in the container).
    private const string QueryRole = "wojtus_query";

    private DiscordDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();
        await _db.Guilds.ExecuteDeleteAsync();
        _db.Guilds.Add(new GuildEntity { DiscordId = SeedGuildId, Name = "seed-guild" });
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ExecuteAsync_Select_ReturnsRowsAsJson()
    {
        var result = await NewService().ExecuteAsync("SELECT name FROM guilds ORDER BY name", CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("seed-guild", doc.RootElement.GetProperty("rows")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_BigintSnowflake_IsSerializedAsString()
    {
        var result = await NewService().ExecuteAsync("SELECT discord_id FROM guilds", CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var snowflake = doc.RootElement.GetProperty("rows")[0].GetProperty("discord_id");
        // The precision guarantee: a bigint must come back as a JSON string, never a number.
        Assert.Equal(JsonValueKind.String, snowflake.ValueKind);
        Assert.Equal(SeedGuildId.ToString(), snowflake.GetString());
    }

    [Fact]
    public async Task ExecuteAsync_PrivilegedFileFunction_IsRejectedByRoleDrop()
    {
        // The headline: as the superuser EF login this would read the file; under SET LOCAL ROLE to the
        // non-superuser role it must be permission-denied. This is what the read-only txn alone misses.
        var result = await NewService().ExecuteAsync(
            "SELECT pg_read_file('/etc/hostname', 0, 100)", CancellationToken.None);

        Assert.DoesNotContain("row_count", result);
        Assert.Contains("permission denied", result);
    }

    [Fact]
    public async Task ExecuteAsync_NonSelectWrite_IsRejectedByAppGuard()
    {
        var result = await NewService().ExecuteAsync(
            "INSERT INTO guilds (discord_id, name) VALUES (1, 'hacked')", CancellationToken.None);

        Assert.DoesNotContain("row_count", result);
        Assert.Contains("Only read-only queries", result);
        await using var verify = NewContext();
        Assert.False(await verify.Guilds.AnyAsync(g => g.Name == "hacked"));
    }

    [Fact]
    public async Task ExecuteAsync_DataModifyingCte_IsRejected()
    {
        // Passes the WITH/SELECT keyword guard, so the read-only txn + non-superuser role are the only
        // things stopping the write.
        var result = await NewService().ExecuteAsync(
            "WITH x AS (INSERT INTO guilds (discord_id, name) VALUES (424242, 'cte-hack') RETURNING name) "
            + "SELECT name FROM x",
            CancellationToken.None);

        Assert.DoesNotContain("row_count", result);
        Assert.Contains("SQL error", result);
        await using var verify = NewContext();
        Assert.False(await verify.Guilds.AnyAsync(g => g.Name == "cte-hack"));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleStatements_AreRejected()
    {
        var result = await NewService().ExecuteAsync("SELECT 1; SELECT 2", CancellationToken.None);

        Assert.Contains("single statement", result);
    }

    [Fact]
    public async Task ExecuteAsync_RowsBeyondCap_AreTruncated()
    {
        await SeedExtraGuildsAsync(5);

        var result = await NewService(rowLimit: 3).ExecuteAsync("SELECT discord_id FROM guilds", CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(3, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_ExactlyRowLimit_IsNotTruncated()
    {
        // Boundary: rows == limit exactly must NOT flag truncation (the N+1 read returns false).
        await SeedExtraGuildsAsync(2); // seed-guild + 2 = 3 rows total

        var result = await NewService(rowLimit: 3).ExecuteAsync("SELECT discord_id FROM guilds", CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(3, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task ExecuteAsync_SlowQuery_IsStoppedByClientTimeout()
    {
        var result = await NewService(timeoutSeconds: 1).ExecuteAsync("SELECT pg_sleep(5)", CancellationToken.None);

        Assert.DoesNotContain("\"row_count\"", result);
        Assert.True(
            result.Contains("SQL error", StringComparison.Ordinal)
            || result.Contains("stopped", StringComparison.Ordinal),
            $"Expected a timeout/error message, got: {result}");
    }

    [Fact]
    public async Task ExecuteAsync_SlowQuery_IsStoppedByServerTimeout_WhenClientTimeoutDisabled()
    {
        // Client CommandTimeout=0 is infinite, so this proves the server-side SET LOCAL statement_timeout
        // actually fires (57014), not just the client timeout.
        var result = await NewService(timeoutSeconds: 0, serverTimeoutSeconds: 1)
            .ExecuteAsync("SELECT pg_sleep(5)", CancellationToken.None);

        Assert.DoesNotContain("\"row_count\"", result);
        Assert.Contains("57014", result); // query_canceled (statement_timeout)
    }

    [Fact]
    public async Task ExecuteAsync_LeavesEfConnectionUsableAndClosed()
    {
        await using var context = NewContext();
        var service = new DatabaseQueryService(context, OptionsFor(), NullLogger<DatabaseQueryService>.Instance);

        await service.ExecuteAsync("SELECT 1 AS n", CancellationToken.None);
        // An error path that actually opens the connection and rolls back (reaches the DB, unlike the
        // app-guard rejection which returns early).
        await service.ExecuteAsync("SELECT pg_read_file('/etc/hostname')", CancellationToken.None);

        // EF on the same context still works (role + txn were reset), and the connection it opened is closed.
        Assert.Equal(1, await context.Guilds.CountAsync());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task ExecuteAsync_NonFiniteFloat_SerializesWithoutThrowing()
    {
        // 'Infinity'::float8 would make STJ's default Strict number handling throw mid-serialize, and
        // that throw escapes the DB-exception catch filters — must come back as a named literal instead.
        var result = await NewService().ExecuteAsync("SELECT 'Infinity'::float8 AS inf", CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.Equal("Infinity", doc.RootElement.GetProperty("rows")[0].GetProperty("inf").GetString());
    }

    [Fact]
    public async Task SchemaHint_ListsTablesWithIdAnnotations()
    {
        await using var context = NewContext();
        var hint = DatabaseSchemaHint.Build(SchemaCatalog.Build(context.Model));

        Assert.Contains("guilds(", hint.Text);
        Assert.Contains("messages(", hint.Text);
        Assert.Contains(":id", hint.Text); // bigint snowflakes are tagged
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRoleNameConfigured_ReportsMisconfigured()
    {
        var service = new DatabaseQueryService(
            NewContext(),
            Options.Create(new ConversationOptions { QueryRoleName = "bad; name" }),
            NullLogger<DatabaseQueryService>.Instance);

        var result = await service.ExecuteAsync("SELECT 1", CancellationToken.None);

        Assert.Contains("misconfigured", result);
    }

    private DatabaseQueryService NewService(int rowLimit = 100, int timeoutSeconds = 10, int serverTimeoutSeconds = 15) =>
        new(NewContext(), OptionsFor(rowLimit, timeoutSeconds, serverTimeoutSeconds), NullLogger<DatabaseQueryService>.Instance);

    private static IOptions<ConversationOptions> OptionsFor(int rowLimit = 100, int timeoutSeconds = 10, int serverTimeoutSeconds = 15) =>
        Options.Create(new ConversationOptions
        {
            QueryRoleName = QueryRole,
            QueryRowLimit = rowLimit,
            QueryTimeoutSeconds = timeoutSeconds,
            QueryServerTimeoutSeconds = serverTimeoutSeconds,
        });

    private async Task SeedExtraGuildsAsync(int count)
    {
        for (var i = 1; i <= count; i++)
            _db.Guilds.Add(new GuildEntity { DiscordId = 10_000UL + (ulong)i, Name = $"extra-{i}" });
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
