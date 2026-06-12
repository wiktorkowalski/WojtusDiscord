using System.Net;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Events;
using DiscordEventService.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class HealthCheckEventRatioTests(PostgresFixture fixture)
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
    public async Task EventRatioDrop_DebouncesAcrossRuns_AndExcludesVoice()
    {
        var now = DateTime.UtcNow;

        // Baseline: 20 events/day for each of the previous 7 days, ~1h before "now" so they land
        // inside the 6h time-of-day baseline window. One normal type and one excluded voice type.
        string[] eventTypes = ["MessageCreated", "VoiceStateUpdated"];
        foreach (var type in eventTypes)
        {
            for (var day = 1; day <= 7; day++)
            {
                for (var i = 0; i < 20; i++)
                    _db.RawEventLogs.Add(new RawEventLogEntity
                    {
                        Id = Guid.NewGuid(),
                        EventType = type,
                        EventJson = "{}",
                        ReceivedAtUtc = now.AddDays(-day).AddHours(-1).AddSeconds(i),
                    });
            }
        }
        // Recent 6h window: nothing seeded for either type -> both read as a near-total drop.
        await _db.SaveChangesAsync();

        var handler = new CapturingHandler();
        await using var provider = new ServiceCollection()
            .AddDbContext<DiscordDbContext>(o => o
                .UseNpgsql(fixture.Container.GetConnectionString())
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider();

        var opts = Options.Create(new HealthCheckOptions
        {
            WebhookUrl = "https://example.test/webhook",
            EventRatioConsecutiveRuns = 3,
        });
        var job = new HealthCheckJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubHttpClientFactory(handler),
            opts,
            NullLogger<HealthCheckJob>.Instance);

        // Runs 1 & 2: drop detected but below the consecutive-run threshold -> stays silent.
        await job.ExecuteAsync(CancellationToken.None);
        await job.ExecuteAsync(CancellationToken.None);
        Assert.Empty(handler.Bodies);

        // Run 3: streak reaches 3 -> alert fires. MessageCreated has an identical baseline/recent
        // profile to the voice type, so its presence proves the collapse *would* trip an alert —
        // the voice type's absence is therefore solely due to the exclusion list, not quiet data.
        await job.ExecuteAsync(CancellationToken.None);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("MessageCreated", body);
        Assert.Contains("1 event type(s)", body);     // exactly one type reported...
        Assert.DoesNotContain("VoiceStateUpdated", body); // ...and the excluded voice type is never it
    }

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient(handler, disposeHandler: false);
    }
}
