using System.Collections.Concurrent;
using System.Diagnostics;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Xunit;

namespace DiscordEventService.Tests;

// UseSlidingInvisibilityTimeout heartbeats fetchedat while the worker is alive, so a job
// outliving InvisibilityTimeout runs exactly once instead of being re-fetched as dead.
public sealed class HangfireSlidingInvisibilityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan InvisibilityTimeout = TimeSpan.FromSeconds(5);

    // Tuned against InvisibilityTimeout: the job must outlive several invisibility
    // windows, and the waits must cover fetch + (for fixed mode) one re-fetch.
    private const int JobRuntimeMs = 12_000;
    private static readonly TimeSpan FirstExecutionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefetchTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(16);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task SlidingInvisibility_JobOutlivingTimeout_ExecutesExactlyOnce()
    {
        using var server = StartServer("hangfire_sliding", useSliding: true, out var storage);

        new BackgroundJobClient(storage).Enqueue(() => SlowJobProbe.Run("sliding", JobRuntimeMs));

        Assert.True(await WaitFor(() => SlowJobProbe.ExecutionCount("sliding") >= 1, FirstExecutionTimeout));
        await Task.Delay(SettleWindow);

        Assert.Equal(1, SlowJobProbe.ExecutionCount("sliding"));
    }

    [Fact]
    public async Task FixedInvisibility_JobOutlivingTimeout_IsRefetched()
    {
        using var server = StartServer("hangfire_fixed", useSliding: false, out var storage);

        try
        {
            new BackgroundJobClient(storage).Enqueue(() => SlowJobProbe.Run("fixed", JobRuntimeMs));

            Assert.True(
                await WaitFor(() => SlowJobProbe.ExecutionCount("fixed") >= 2, RefetchTimeout),
                "job outliving a fixed InvisibilityTimeout was expected to be re-fetched");
        }
        finally
        {
            SlowJobProbe.Stop("fixed");
        }
    }

    private BackgroundJobServer StartServer(string schema, bool useSliding, out PostgreSqlStorage storage)
    {
        var options = new PostgreSqlStorageOptions
        {
            SchemaName = schema,
            UseSlidingInvisibilityTimeout = useSliding,
            InvisibilityTimeout = InvisibilityTimeout,
            QueuePollInterval = TimeSpan.FromMilliseconds(250)
        };
        storage = new PostgreSqlStorage(new NpgsqlConnectionFactory(fixture.Container.GetConnectionString(), options), options);

        return new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                WorkerCount = 2,
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            },
            storage);
    }

    private static async Task<bool> WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
                return true;

            await Task.Delay(PollDelay);
        }

        return condition();
    }
}

// Public so Hangfire's activator can invoke it from the persisted job payload.
public static class SlowJobProbe
{
    private static readonly ConcurrentDictionary<string, int> _executions = new();
    private static readonly ConcurrentDictionary<string, bool> _stopped = new();

    public static int ExecutionCount(string key) => _executions.GetValueOrDefault(key);

    public static void Stop(string key) => _stopped[key] = true;

    public static void Run(string key, int milliseconds)
    {
        _executions.AddOrUpdate(key, 1, (_, count) => count + 1);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds && !_stopped.ContainsKey(key))
            Thread.Sleep(100);
    }
}
