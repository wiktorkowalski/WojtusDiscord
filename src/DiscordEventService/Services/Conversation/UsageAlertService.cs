using System.Globalization;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// §3 soft cost-cap alerting (#269) over the conversation_usage ledger: after every
// turn, sum the calendar-UTC windows and DM the admins when a cap's warn level is
// crossed — once per (cap, window, level), deduped through the usage_alerts table.
// Sums deliberately include failed/retried attempts (they may bill, #268) — no
// Failed filter. Alert-only by design: the loop never refuses a turn, and every
// failure in here is logged and swallowed so the alert path can never break the
// reply. Scoped over the request DbContext; dual-registered via CoreServiceTypes.
internal sealed class UsageAlertService(
    DiscordDbContext db,
    IOptions<ConversationOptions> conversationOptions,
    IUsageAlertNotifier notifier,
    ILogger<UsageAlertService> logger)
{
    // The monthly caps warn twice, the daily runaway tripwire once (#269 design).
    private static readonly int[] MonthlyLevels = [80, 100];
    private static readonly int[] DailyLevels = [100];

    // The check runs post-turn outside the turn's (possibly already-cancelled)
    // timeout token, so it carries its own ceiling against a hung DB or Discord call.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    public async Task CheckAndAlertAsync(ulong invokerId)
    {
        try
        {
            using var cts = new CancellationTokenSource(CheckTimeout);
            await CheckCapsAsync(invokerId, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cost-cap alert check failed; the turn's reply is unaffected");
        }
    }

    private async Task CheckCapsAsync(ulong invokerId, CancellationToken cancellationToken)
    {
        var caps = conversationOptions.Value.CostAlerts;
        if (!caps.Enabled)
            return;

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var dayStart = now.Date;

        // A cap of 0/null is disabled — the pattern's > 0 covers both.
        if (caps.GlobalMonthlyUsd is > 0 and double globalMonthly)
        {
            await CheckCapAsync(UsageAlertCap.GlobalMonthly, monthStart, MonthlyLevels,
                await SumSpendAsync(monthStart, null, cancellationToken),
                globalMonthly, null, cancellationToken);
        }

        if (caps.PerUserMonthlyUsd is > 0 and double perUserMonthly)
        {
            await CheckCapAsync(UsageAlertCap.PerUserMonthly, monthStart, MonthlyLevels,
                await SumSpendAsync(monthStart, invokerId, cancellationToken),
                perUserMonthly, invokerId, cancellationToken);
        }

        if (caps.GlobalDailyUsd is > 0 and double globalDaily)
        {
            await CheckCapAsync(UsageAlertCap.GlobalDaily, dayStart, DailyLevels,
                await SumSpendAsync(dayStart, null, cancellationToken),
                globalDaily, null, cancellationToken);
        }
    }

    private async Task CheckCapAsync(
        UsageAlertCap cap, DateTime windowStartUtc, int[] levels, double spentUsd,
        double limitUsd, ulong? userDiscordId, CancellationToken cancellationToken)
    {
        foreach (var level in levels)
        {
            if (spentUsd < limitUsd * level / 100)
                continue;

            // Insert-if-absent on the (cap, window, level, user) unique index is the
            // dedup: only the writer that actually created the row sends the DM, so
            // each crossing alerts exactly once per window — restart-proof.
            var (_, inserted) = await db.UsageAlerts.GetOrInsertAsync(
                a => a.Cap == cap && a.WindowStartUtc == windowStartUtc
                    && a.Level == level && a.UserDiscordId == userDiscordId,
                () => new UsageAlertEntity
                {
                    Cap = cap,
                    WindowStartUtc = windowStartUtc,
                    Level = level,
                    UserDiscordId = userDiscordId,
                    SpentUsd = spentUsd,
                    LimitUsd = limitUsd,
                    CreatedAtUtc = DateTime.UtcNow,
                },
                cancellationToken);
            if (!inserted)
                continue;

            logger.LogInformation(
                "Cost cap {Cap} crossed {Level}% ({Spent:0.00}/{Limit:0.00} USD) for window {WindowStart:yyyy-MM-dd}",
                cap, level, spentUsd, limitUsd, windowStartUtc);

            var message = await BuildMessageAsync(
                cap, level, spentUsd, limitUsd, windowStartUtc, userDiscordId, cancellationToken);
            await notifier.NotifyAdminsAsync(message, cancellationToken);
        }
    }

    private Task<double> SumSpendAsync(
        DateTime windowStartUtc, ulong? invokerId, CancellationToken cancellationToken)
    {
        var rows = db.ConversationUsage.Where(u => u.CreatedAtUtc >= windowStartUtc);
        if (invokerId is not null)
            rows = rows.Where(u => u.InvokerId == invokerId);
        return rows.SumAsync(u => u.CostUsd ?? 0, cancellationToken);
    }

    private async Task<string> BuildMessageAsync(
        UsageAlertCap cap, int level, double spentUsd, double limitUsd,
        DateTime windowStartUtc, ulong? userDiscordId, CancellationToken cancellationToken)
    {
        var emoji = level >= 100 ? "🚨" : "⚠️";
        var spend = $"{Usd(spentUsd)} z {Usd(limitUsd)}";

        switch (cap)
        {
            case UsageAlertCap.PerUserMonthly:
                return $"{emoji} Koszt rozmów: <@{userDiscordId}> przekroczył {level}% " +
                    $"miesięcznego limitu ({spend}, okno {windowStartUtc:yyyy-MM}).";

            case UsageAlertCap.GlobalDaily:
                return $"{emoji} Koszt rozmów: dzienny limit awaryjny przekroczył {level}% " +
                    $"({spend}, {windowStartUtc:yyyy-MM-dd}).\n" +
                    $"Najwięksi: {await TopSpendersAsync(windowStartUtc, cancellationToken)}";

            default:
                return $"{emoji} Koszt rozmów: globalny limit miesięczny przekroczył {level}% " +
                    $"({spend}, okno {windowStartUtc:yyyy-MM}).\n" +
                    $"Najwięksi: {await TopSpendersAsync(windowStartUtc, cancellationToken)}";
        }
    }

    // Per-invoker sums over the cap's window, biggest first — the reason the ledger
    // carries invoker_id (#256): a global-cap alert should say who is spending.
    private async Task<string> TopSpendersAsync(
        DateTime windowStartUtc, CancellationToken cancellationToken)
    {
        var top = await db.ConversationUsage
            .Where(u => u.CreatedAtUtc >= windowStartUtc)
            .GroupBy(u => u.InvokerId)
            .Select(g => new { InvokerId = g.Key, SpentUsd = g.Sum(u => u.CostUsd ?? 0) })
            .OrderByDescending(x => x.SpentUsd)
            .Take(conversationOptions.Value.CostAlerts.TopSpendersCount)
            .ToListAsync(cancellationToken);

        return string.Join(", ", top.Select(t => $"<@{t.InvokerId}> {Usd(t.SpentUsd)}"));
    }

    private static string Usd(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"${value:0.00}");
}
