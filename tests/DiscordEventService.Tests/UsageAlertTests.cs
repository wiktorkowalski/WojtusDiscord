using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Conversations;
using DiscordEventService.Services.Conversation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// #269 soft cost-cap alerting, against a real Postgres (Testcontainers, no DB mocking).
// The seeded conversation_usage ledger is the input; the recording notifier is the
// output — thresholds, 80/100 dedup, window rollover, disabled caps and top-spenders
// are all proven here, the Discord DM transport itself is live-verified on the dev bot.
public sealed class UsageAlertTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const ulong InvokerId = 42UL;
    private const ulong OtherInvokerId = 43UL;

    private DiscordDbContext _db = null!;
    private Guid _conversationId;

    public async Task InitializeAsync()
    {
        _db = NewContext();
        await _db.Database.MigrateAsync();

        await _db.UsageAlerts.ExecuteDeleteAsync();
        await _db.ConversationUsage.ExecuteDeleteAsync();
        await _db.ConversationMessages.ExecuteDeleteAsync();
        await _db.Conversations.ExecuteDeleteAsync();

        var conversation = new ConversationEntity
        {
            ChannelDiscordId = 1001UL,
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityAtUtc = DateTime.UtcNow,
        };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync();
        _conversationId = conversation.Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task BelowAllThresholds_StaysSilent()
    {
        await SeedSpendAsync(InvokerId, 1.00);

        var notifier = await RunCheckAsync();

        Assert.Empty(notifier.Messages);
        Assert.Empty(await _db.UsageAlerts.ToListAsync());
    }

    [Fact]
    public async Task GlobalMonthly_Crossing80_AlertsOnce_ThenDedups()
    {
        // $8.50 of $10 = 85% — over the 80% warn level, under 100%.
        await SeedSpendAsync(InvokerId, 8.50);

        var first = await RunCheckAsync(GlobalMonthlyOnly);
        var second = await RunCheckAsync(GlobalMonthlyOnly);

        var message = Assert.Single(first.Messages);
        Assert.Contains("80%", message);
        Assert.Empty(second.Messages);

        var alert = Assert.Single(await _db.UsageAlerts.ToListAsync());
        Assert.Equal(UsageAlertCap.GlobalMonthly, alert.Cap);
        Assert.Equal(80, alert.Level);
        Assert.Null(alert.UserDiscordId);
        Assert.Equal(MonthStart(), alert.WindowStartUtc);
        Assert.Equal(8.50, alert.SpentUsd, precision: 6);
        Assert.Equal(10, alert.LimitUsd, precision: 6);
    }

    [Fact]
    public async Task GlobalMonthly_Crossing100AfterAn80Alert_AlertsOnceMore()
    {
        await SeedSpendAsync(InvokerId, 8.50);
        await RunCheckAsync(GlobalMonthlyOnly);

        await SeedSpendAsync(InvokerId, 2.00); // total $10.50 — past 100%
        var notifier = await RunCheckAsync(GlobalMonthlyOnly);

        var message = Assert.Single(notifier.Messages);
        Assert.Contains("100%", message);
        Assert.Equal([80, 100], (await _db.UsageAlerts.OrderBy(a => a.Level).Select(a => a.Level).ToListAsync()));
    }

    [Fact]
    public async Task SingleJumpPastBothLevels_AlertsBothOnce()
    {
        await SeedSpendAsync(InvokerId, 11.00);

        var notifier = await RunCheckAsync(GlobalMonthlyOnly);

        Assert.Equal(2, notifier.Messages.Count);
        Assert.Equal([80, 100], (await _db.UsageAlerts.OrderBy(a => a.Level).Select(a => a.Level).ToListAsync()));
    }

    [Fact]
    public async Task DailyTripwire_SilentAt80_FiresAt100Only()
    {
        var notifierAt85 = await RunCheckAsync(MonthlyCapsOff, seedBefore: 1.70); // 85% of $2
        Assert.Empty(notifierAt85.Messages);

        await SeedSpendAsync(InvokerId, 0.40); // total $2.10 — over
        var notifierOver = await RunCheckAsync(MonthlyCapsOff);

        var message = Assert.Single(notifierOver.Messages);
        Assert.Contains("100%", message);
        var alert = Assert.Single(await _db.UsageAlerts.ToListAsync());
        Assert.Equal(UsageAlertCap.GlobalDaily, alert.Cap);
        Assert.Equal(100, alert.Level);
        Assert.Equal(DayStart(), alert.WindowStartUtc);
    }

    [Fact]
    public async Task PerUserMonthly_AlertsTheCrossingInvokerOnly()
    {
        // Invoker 42 at $2.50 of $3 = 83%; invoker 43 far below.
        await SeedSpendAsync(InvokerId, 2.50);
        await SeedSpendAsync(OtherInvokerId, 0.10);

        var notifier = await RunCheckAsync(options =>
        {
            options.CostAlerts.GlobalMonthlyUsd = null; // isolate the per-user cap
            options.CostAlerts.GlobalDailyUsd = null;
        });

        var message = Assert.Single(notifier.Messages);
        Assert.Contains($"<@{InvokerId}>", message);
        var alert = Assert.Single(await _db.UsageAlerts.ToListAsync());
        Assert.Equal(UsageAlertCap.PerUserMonthly, alert.Cap);
        Assert.Equal(InvokerId, alert.UserDiscordId);

        // The other invoker's own post-turn check is below threshold — no new alert.
        var otherNotifier = await RunCheckAsync(options =>
        {
            options.CostAlerts.GlobalMonthlyUsd = null;
            options.CostAlerts.GlobalDailyUsd = null;
        }, invokerId: OtherInvokerId);
        Assert.Empty(otherNotifier.Messages);
    }

    [Fact]
    public async Task WindowRollover_LastMonthsAlertDoesNotSuppressThisMonth()
    {
        // Last month crossed and alerted; this month crosses again.
        _db.UsageAlerts.Add(new UsageAlertEntity
        {
            Cap = UsageAlertCap.GlobalMonthly,
            WindowStartUtc = MonthStart().AddMonths(-1),
            Level = 80,
            SpentUsd = 9.00,
            LimitUsd = 10,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-1),
        });
        await _db.SaveChangesAsync();
        await SeedSpendAsync(InvokerId, 8.50);

        var notifier = await RunCheckAsync(GlobalMonthlyOnly);

        Assert.Single(notifier.Messages);
        Assert.Equal(2, await _db.UsageAlerts.CountAsync());
    }

    [Fact]
    public async Task LastMonthsSpend_DoesNotCountTowardThisWindow()
    {
        await SeedSpendAsync(InvokerId, 9.00, LastMonth());
        await SeedSpendAsync(InvokerId, 1.00);

        var notifier = await RunCheckAsync(GlobalMonthlyOnly);

        Assert.Empty(notifier.Messages);
    }

    [Fact]
    public async Task DisabledCapsAndMasterSwitch_AreInert()
    {
        await SeedSpendAsync(InvokerId, 999.0);

        var capsOff = await RunCheckAsync(options =>
        {
            options.CostAlerts.GlobalMonthlyUsd = 0;
            options.CostAlerts.PerUserMonthlyUsd = null;
            options.CostAlerts.GlobalDailyUsd = 0;
        });
        Assert.Empty(capsOff.Messages);

        var masterOff = await RunCheckAsync(options => options.CostAlerts.Enabled = false);
        Assert.Empty(masterOff.Messages);

        Assert.Empty(await _db.UsageAlerts.ToListAsync());
    }

    [Fact]
    public async Task GlobalAlert_ListsTopSpenders()
    {
        await SeedSpendAsync(InvokerId, 6.00);
        await SeedSpendAsync(OtherInvokerId, 3.00);

        var notifier = await RunCheckAsync(GlobalMonthlyOnly);

        var message = Assert.Single(notifier.Messages);
        Assert.Contains($"<@{InvokerId}>", message);
        Assert.Contains($"<@{OtherInvokerId}>", message);
        // Ordered by spend: the bigger spender is listed first.
        Assert.True(message.IndexOf($"<@{InvokerId}>", StringComparison.Ordinal)
            < message.IndexOf($"<@{OtherInvokerId}>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedAttemptRows_CountTowardTheSums()
    {
        // Failed/retried attempts may still bill (#268) — sums must include them.
        await SeedSpendAsync(InvokerId, 4.30);
        await SeedSpendAsync(InvokerId, 4.30, failed: true);

        var notifier = await RunCheckAsync(GlobalMonthlyOnly);

        var message = Assert.Single(notifier.Messages);
        Assert.Contains("80%", message);
    }

    [Fact]
    public async Task NotifierFailure_IsSwallowed_AndTheAlertStaysRecorded()
    {
        await SeedSpendAsync(InvokerId, 8.50);

        var service = BuildService(new ThrowingNotifier(), BuildOptions(GlobalMonthlyOnly));
        await service.CheckAndAlertAsync(InvokerId); // must not throw

        Assert.Single(await _db.UsageAlerts.ToListAsync());
    }

    private static void MonthlyCapsOff(ConversationOptions options)
    {
        options.CostAlerts.GlobalMonthlyUsd = null;
        options.CostAlerts.PerUserMonthlyUsd = null;
    }

    private static void GlobalMonthlyOnly(ConversationOptions options)
    {
        options.CostAlerts.PerUserMonthlyUsd = null;
        options.CostAlerts.GlobalDailyUsd = null;
    }

    private async Task<RecordingNotifier> RunCheckAsync(
        Action<ConversationOptions>? configure = null, double? seedBefore = null, ulong invokerId = InvokerId)
    {
        if (seedBefore is not null)
            await SeedSpendAsync(invokerId, seedBefore.Value);

        var notifier = new RecordingNotifier();
        await BuildService(notifier, BuildOptions(configure)).CheckAndAlertAsync(invokerId);
        return notifier;
    }

    private UsageAlertService BuildService(IUsageAlertNotifier notifier, IOptions<ConversationOptions> options) =>
        new(NewContext(), options, notifier, NullLogger<UsageAlertService>.Instance);

    private static IOptions<ConversationOptions> BuildOptions(Action<ConversationOptions>? configure)
    {
        var options = new ConversationOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    private async Task SeedSpendAsync(
        ulong invokerId, double costUsd, DateTime? createdAtUtc = null, bool failed = false)
    {
        _db.ConversationUsage.Add(new ConversationUsageEntity
        {
            ConversationId = _conversationId,
            InvokerId = invokerId,
            TurnIndex = 0,
            Round = 1,
            Attempt = failed ? 2 : 1,
            Model = "test-model",
            CostUsd = costUsd,
            LatencyMs = 10,
            Failed = failed,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private static DateTime MonthStart()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static DateTime DayStart() => DateTime.UtcNow.Date;

    // Squarely inside last month's window regardless of today's date.
    private static DateTime LastMonth() => MonthStart().AddDays(-15);

    private DiscordDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DiscordDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new DiscordDbContext(options);
    }

    private sealed class RecordingNotifier : IUsageAlertNotifier
    {
        public List<string> Messages { get; } = [];

        public Task NotifyAdminsAsync(string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotifier : IUsageAlertNotifier
    {
        public Task NotifyAdminsAsync(string message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("DM refused");
    }
}
