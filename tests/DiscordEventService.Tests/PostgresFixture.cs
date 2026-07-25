using DiscordEventService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DiscordEventService.Tests;

// One container for the suite, one database per class: cloning a pre-migrated template is a file
// copy rather than a 29-migration replay, and keeping a database per class is what lets the
// truncation-based isolation and whole-table assertions stay correct under xUnit's parallelism.
// Public because the public test classes name it in IClassFixture<>.
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "migrated_template";

    // uuidv7() default value SQL in the migrations requires PostgreSQL 18. Every class now shares
    // one server's connection budget instead of having a private one, so raise it well clear of the
    // measured peak (63 backends) rather than sitting two thirds into the default 100.
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .WithCommand("-c", "max_connections=300")
        .Build();

    private static readonly Lazy<Task<string>> Template = new(EnsureTemplateAsync);

    // Postgres refuses to clone a template that has an open session, so clones cannot overlap.
    private static readonly SemaphoreSlim CloneGate = new(1, 1);

    private static int _databaseCounter;

    // Points at this class's own cloned database, not the container's default one.
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var admin = await Template.Value;
        var database = $"test_{Interlocked.Increment(ref _databaseCounter)}";

        await CloneGate.WaitAsync();
        try
        {
            await ExecuteAsync(admin, $"CREATE DATABASE {database} TEMPLATE {TemplateDatabase}");
        }
        finally
        {
            CloneGate.Release();
        }

        ConnectionString = WithDatabase(admin, database);
    }

    // The container outlives every fixture instance, so it is not disposed here; Testcontainers'
    // reaper removes it when the test process exits. Cloned databases die with the container.
    // The pool is cleared, though: Npgsql keys pools by connection string and holds physical
    // connections for ConnectionIdleLifetime (300s, longer than the whole suite), so without this
    // the live count grows with total classes rather than concurrent ones.
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        return Task.CompletedTask;
    }

    private static async Task<string> EnsureTemplateAsync()
    {
        await Container.StartAsync();
        var admin = Container.GetConnectionString();
        await ExecuteAsync(admin, $"CREATE DATABASE {TemplateDatabase}");

        // Pooling=false: a pooled connection outlives the DbContext and would make every later
        // CREATE DATABASE ... TEMPLATE fail with "source database is being accessed by other users".
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(WithDatabase(admin, TemplateDatabase, pooling: false))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new DiscordDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        NpgsqlConnection.ClearAllPools();

        // Nothing may connect to the template again. Without this, a stray connection turns into
        // an intermittent clone failure; with it, the mistake fails loudly and immediately.
        await ExecuteAsync(admin, $"ALTER DATABASE {TemplateDatabase} WITH ALLOW_CONNECTIONS false");

        return admin;
    }

    private static string WithDatabase(string connectionString, string database, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database, Pooling = pooling }
            .ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
