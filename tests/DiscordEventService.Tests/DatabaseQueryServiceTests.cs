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
    public async Task RunAsync_Select_ReturnsRowsAsJson()
    {
        var (result, failed) = await NewService().RunAsync("SELECT name FROM guilds ORDER BY name", CancellationToken.None);

        Assert.False(failed);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("seed-guild", doc.RootElement.GetProperty("rows")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task RunAsync_BigintSnowflake_IsSerializedAsString()
    {
        var (result, failed) = await NewService().RunAsync("SELECT discord_id FROM guilds", CancellationToken.None);

        Assert.False(failed);
        using var doc = JsonDocument.Parse(result);
        var snowflake = doc.RootElement.GetProperty("rows")[0].GetProperty("discord_id");
        // The precision guarantee: a bigint must come back as a JSON string, never a number.
        Assert.Equal(JsonValueKind.String, snowflake.ValueKind);
        Assert.Equal(SeedGuildId.ToString(), snowflake.GetString());
    }

    [Fact]
    public async Task RunAsync_PrivilegedFileFunction_IsRejectedByRoleDrop()
    {
        // The headline: as the superuser EF login this would read the file; under SET LOCAL ROLE to the
        // non-superuser role it must be permission-denied. This is what the read-only txn alone misses.
        var (result, failed) = await NewService().RunAsync(
            "SELECT pg_read_file('/etc/hostname', 0, 100)", CancellationToken.None);

        Assert.True(failed);
        Assert.DoesNotContain("row_count", result);
        Assert.Contains("permission denied", result);
    }

    [Fact]
    public async Task RunAsync_NonSelectWrite_IsRejectedByAppGuard()
    {
        var (result, failed) = await NewService().RunAsync(
            "INSERT INTO guilds (discord_id, name) VALUES (1, 'hacked')", CancellationToken.None);

        Assert.True(failed);
        Assert.DoesNotContain("row_count", result);
        Assert.Contains("Only read-only queries", result);
        await using var verify = NewContext();
        Assert.False(await verify.Guilds.AnyAsync(g => g.Name == "hacked"));
    }

    [Fact]
    public async Task RunAsync_DataModifyingCte_IsRejected()
    {
        // Passes the WITH/SELECT keyword guard, so the read-only txn + non-superuser role are the only
        // things stopping the write.
        var (result, failed) = await NewService().RunAsync(
            "WITH x AS (INSERT INTO guilds (discord_id, name) VALUES (424242, 'cte-hack') RETURNING name) "
            + "SELECT name FROM x",
            CancellationToken.None);

        Assert.True(failed);
        Assert.DoesNotContain("row_count", result);
        Assert.Contains("SQL error", result);
        await using var verify = NewContext();
        Assert.False(await verify.Guilds.AnyAsync(g => g.Name == "cte-hack"));
    }

    [Fact]
    public async Task RunAsync_MultipleStatements_AreRejected()
    {
        var (result, failed) = await NewService().RunAsync("SELECT 1; SELECT 2", CancellationToken.None);

        Assert.True(failed);
        Assert.Contains("single statement", result);
    }

    [Fact]
    public async Task RunAsync_RowsBeyondCap_AreTruncated()
    {
        await SeedExtraGuildsAsync(5);

        var (result, failed) = await NewService(rowLimit: 3).RunAsync("SELECT discord_id FROM guilds", CancellationToken.None);

        Assert.False(failed);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(3, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ExactlyRowLimit_IsNotTruncated()
    {
        // Boundary: rows == limit exactly must NOT flag truncation (the N+1 read returns false).
        await SeedExtraGuildsAsync(2); // seed-guild + 2 = 3 rows total

        var (result, failed) = await NewService(rowLimit: 3).RunAsync("SELECT discord_id FROM guilds", CancellationToken.None);

        Assert.False(failed);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(3, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_SlowQuery_IsStoppedByClientTimeout()
    {
        var (result, failed) = await NewService(timeoutSeconds: 1).RunAsync("SELECT pg_sleep(5)", CancellationToken.None);

        Assert.True(failed);
        Assert.DoesNotContain("\"row_count\"", result);
        Assert.True(
            result.Contains("SQL error", StringComparison.Ordinal)
            || result.Contains("stopped", StringComparison.Ordinal),
            $"Expected a timeout/error message, got: {result}");
    }

    [Fact]
    public async Task RunAsync_SlowQuery_IsStoppedByServerTimeout_WhenClientTimeoutDisabled()
    {
        // Client CommandTimeout=0 is infinite, so this proves the server-side SET LOCAL statement_timeout
        // actually fires (57014), not just the client timeout.
        var (result, failed) = await NewService(timeoutSeconds: 0, serverTimeoutSeconds: 1)
            .RunAsync("SELECT pg_sleep(5)", CancellationToken.None);

        Assert.True(failed);
        Assert.DoesNotContain("\"row_count\"", result);
        Assert.Contains("57014", result); // query_canceled (statement_timeout)
    }

    [Fact]
    public async Task RunAsync_LeavesEfConnectionUsableAndClosed()
    {
        await using var context = NewContext();
        var service = new DatabaseQueryService(context, OptionsFor(), NullLogger<DatabaseQueryService>.Instance);

        Assert.False((await service.RunAsync("SELECT 1 AS n", CancellationToken.None)).Failed);
        // An error path that actually opens the connection and rolls back (reaches the DB, unlike the
        // app-guard rejection which returns early).
        Assert.True((await service.RunAsync("SELECT pg_read_file('/etc/hostname')", CancellationToken.None)).Failed);

        // EF on the same context still works (role + txn were reset), and the connection it opened is closed.
        Assert.Equal(1, await context.Guilds.CountAsync());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task RunAsync_NonFiniteFloat_SerializesWithoutThrowing()
    {
        // 'Infinity'::float8 would make STJ's default Strict number handling throw mid-serialize, and
        // that throw escapes the DB-exception catch filters — must come back as a named literal instead.
        var (result, failed) = await NewService().RunAsync("SELECT 'Infinity'::float8 AS inf", CancellationToken.None);

        Assert.False(failed);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.Equal("Infinity", doc.RootElement.GetProperty("rows")[0].GetProperty("inf").GetString());
    }

    // #448: Npgsql maps these to int[], DateTime[], double[], none of which the old switch knew,
    // so they came back as "System.Int32[]".
    [Theory]
    [InlineData("SELECT ARRAY[1, 2, 3]::int4[] AS v", "[1,2,3]")]
    [InlineData("SELECT ARRAY[1.5, 2]::float8[] AS v", "[1.5,2]")]
    [InlineData("SELECT ARRAY['2026-10-18T12:00:00Z']::timestamptz[] AS v", "[\"2026-10-18T12:00:00.0000000Z\"]")]
    [InlineData("SELECT ARRAY[9007199254740993]::int8[] AS v", "[\"9007199254740993\"]")]
    [InlineData("SELECT ARRAY['a', NULL]::text[] AS v", "[\"a\",null]")]
    [InlineData("SELECT '{\"a\": 1}'::jsonb AS v", "\"{\\\"a\\\": 1}\"")]
    [InlineData("SELECT '\\xdeadbeef'::bytea AS v", "\"DEADBEEF\"")]
    public async Task RunAsync_ColumnTypes_ComeBackAsJsonValues(string sql, string expected)
    {
        var (result, failed) = await NewService().RunAsync(sql, CancellationToken.None);

        Assert.False(failed, result);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(expected, doc.RootElement.GetProperty("rows")[0].GetProperty("v").GetRawText());
    }

    [Fact]
    public async Task RunAsync_LongArray_IsCutWithAMarker()
    {
        var (result, failed) = await NewService().RunAsync(
            "SELECT array_agg(g) AS v FROM generate_series(1, 500) g", CancellationToken.None);

        Assert.False(failed);
        using var doc = JsonDocument.Parse(result);
        var values = doc.RootElement.GetProperty("rows")[0].GetProperty("v");
        Assert.Equal(101, values.GetArrayLength());
        Assert.Equal(DatabaseQueryService.TruncationMarker, values[100].GetString());
    }

    [Fact]
    public async Task RunAsync_ResultOverBudget_IsTruncatedWithANote()
    {
        // The live case from #448: 30 rows of two 2000-char values was 120 KB.
        var (result, failed) = await NewService().RunAsync(
            "SELECT g, repeat('x', 2000) AS a, repeat('y', 2000) AS b FROM generate_series(1, 30) g",
            CancellationToken.None);

        Assert.False(failed);
        Assert.True(result.Length <= DatabaseQueryService.MaxResultLength + 500, $"length {result.Length}");
        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.InRange(doc.RootElement.GetProperty("row_count").GetInt32(), 1, 29);
        Assert.Contains("characters", doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task RunAsync_SingleRowOverBudget_ComesBackEmptyWithANote()
    {
        // 100 elements of 2000 chars pass the field and array caps but are ~200 KB of JSON in one row.
        var (result, failed) = await NewService().RunAsync(
            "SELECT array_agg(repeat('x', 2000)) AS v FROM generate_series(1, 100)", CancellationToken.None);

        Assert.False(failed);
        Assert.True(result.Length < 1000, $"length {result.Length}");
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("row_count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Contains("characters", doc.RootElement.GetProperty("note").GetString());
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

    // #384: the meme search log holds what each person typed, and any member can run query_database.
    // The role gets SELECT on a new table by default, so the migration has to take it back.
    // #444: the same for the assistant's conversations, DMs included, and the usage ledger that ties a
    // row to its invoker.
    [Theory]
    [InlineData("meme_search_log")]
    [InlineData("meme_search_log_results")]
    [InlineData("conversation")]
    [InlineData("conversation_message")]
    [InlineData("conversation_usage")]
    [InlineData("usage_alerts")]
    public async Task RunAsync_PrivateTable_IsDeniedToTheQueryRole(string table)
    {
        var (text, failed) = await NewService().RunAsync($"SELECT count(*) FROM {table}", CancellationToken.None);

        Assert.True(failed);
        Assert.DoesNotContain("row_count", text);
        Assert.Contains("permission denied", text);
    }

    // The same gate read from the catalog: no privilege of any kind, for the query role and for
    // PUBLIC (every role inherits from it). A control table shows the check can say "true".
    [Theory]
    [InlineData("meme_search_log")]
    [InlineData("meme_search_log_results")]
    [InlineData("conversation")]
    [InlineData("conversation_message")]
    [InlineData("conversation_usage")]
    [InlineData("usage_alerts")]
    public async Task PrivateTable_GrantsNothingToTheQueryRoleOrToPublic(string table)
    {
        var privileges = await _db.Database.SqlQuery<bool>($"""
            SELECT has_table_privilege(r.role, {table}, p.privilege) AS "Value"
            FROM (VALUES ({QueryRole}), ('public')) AS r(role)
            CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('TRUNCATE'), ('REFERENCES'), ('TRIGGER')) AS p(privilege)
            """).ToListAsync();
        var control = await _db.Database.SqlQuery<bool>(
            $"""SELECT has_table_privilege({QueryRole}, 'meme_index', 'SELECT') AS "Value" """).SingleAsync();

        Assert.Equal(14, privileges.Count);
        Assert.DoesNotContain(true, privileges);
        Assert.True(control);
    }

    // The hint has one "name(columns)" line per table; matching the line start keeps "conversation"
    // from hitting conversation_id, a column of other tables.
    [Theory]
    [InlineData("meme_search_log")]
    [InlineData("meme_search_log_results")]
    [InlineData("conversation")]
    [InlineData("conversation_message")]
    [InlineData("conversation_usage")]
    [InlineData("usage_alerts")]
    public async Task SchemaHint_DoesNotNameThePrivateTable(string table)
    {
        await using var context = NewContext();
        var catalog = SchemaCatalog.Build(context.Model);

        Assert.DoesNotContain(
            DatabaseSchemaHint.Build(catalog).Text.Split('\n'),
            line => line.StartsWith($"{table}(", StringComparison.Ordinal));
        Assert.False(catalog.TryGetTable(table, out _));
    }

    [Fact]
    public async Task RunAsync_InvalidRoleNameConfigured_ReportsMisconfigured()
    {
        var service = new DatabaseQueryService(
            NewContext(),
            Options.Create(new ConversationOptions { QueryRoleName = "bad; name" }),
            NullLogger<DatabaseQueryService>.Instance);

        var (result, failed) = await service.RunAsync("SELECT 1", CancellationToken.None);

        Assert.True(failed);
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
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }
}
