using System.Globalization;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Controllers;

/// <summary>
/// Per-person profile: all-time activity totals, a favourite emote, busiest channel,
/// a 14-day message sparkline, current presence status and the name-change history.
/// Keyed by Discord snowflake (route param), 404 when the snowflake is unknown.
/// </summary>
[ApiController]
[Route("api/people")]
public sealed class PeopleController(DiscordDbContext db) : ControllerBase
{
    private const string Tz = "Europe/Warsaw";

    [HttpGet("{discordId:long}/profile")]
    public async Task<ActionResult<ProfileDto>> Profile(long discordId, CancellationToken ct)
    {
        // Snowflakes are stored as unchecked (long)ulong; current ids are < 2^63 so the
        // route's long maps directly to the stored value.
        var discordSf = unchecked((ulong)discordId);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.DiscordId == discordSf)
            .Select(u => new
            {
                u.Id,
                u.DiscordId,
                u.Username,
                u.GlobalName,
                u.AvatarHash,
                u.IsBot,
                u.FirstSeenUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return NotFound();
        }

        var userId = user.Id;

        var messageCount = await db.Messages.AsNoTracking()
            .LongCountAsync(m => m.AuthorId == userId, ct);

        var memeCount = await db.Messages.AsNoTracking()
            .LongCountAsync(m => m.AuthorId == userId && (m.HasAttachments || m.HasEmbeds), ct);

        // Present reactions on this user's messages (see ReactionEventQueryExtensions).
        var reactionsReceived = await db.ReactionEvents.AsNoTracking().WherePresent()
            .Join(
                db.Messages.AsNoTracking().Where(m => m.AuthorId == userId),
                r => r.MessageDiscordId,
                m => m.DiscordId,
                (r, m) => r.Id)
            .LongCountAsync(ct);

        var voiceMinutes = await VoiceMinutesAsync(discordSf, ct);
        var onlineMinutes = await OnlineMinutesAsync(discordSf, ct);

        // Top emote the user gave (present reactions only).
        var favoriteEmote = await db.ReactionEvents.AsNoTracking().WherePresent()
            .Where(r => r.UserDiscordId == discordSf)
            .GroupBy(r => new { r.EmoteName, r.EmoteDiscordId })
            .Select(g => new { g.Key.EmoteName, g.Key.EmoteDiscordId, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(ct);

        var favoriteEmoteDto = favoriteEmote is null
            ? null
            : new ProfileFavoriteEmoteDto(
                favoriteEmote.EmoteName, favoriteEmote.EmoteDiscordId, favoriteEmote.EmoteDiscordId is > 0);

        // Channel the user posted in most.
        var busiestChannel = await db.Messages.AsNoTracking()
            .Where(m => m.AuthorId == userId)
            .GroupBy(m => new { m.Channel.Name, m.Channel.DiscordId })
            .Select(g => new { g.Key.Name, g.Key.DiscordId, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync(ct);

        var busiestChannelDto = busiestChannel is null
            ? null
            : new ProfileBusiestChannelDto(busiestChannel.Name, busiestChannel.DiscordId);

        var messagesDaily14 = await MessagesDaily14Async(userId, ct);

        // Current overall status from the latest presence row, by client-status priority
        // (online > idle > dnd > offline); "offline" when there are no presence rows.
        var latestPresence = await db.PresenceEvents.AsNoTracking()
            .Where(p => p.UserDiscordId == discordSf)
            .OrderByDescending(p => p.ReceivedAtUtc)
            .ThenByDescending(p => p.Id)
            .Select(p => new { p.DesktopStatusAfter, p.MobileStatusAfter, p.WebStatusAfter })
            .FirstOrDefaultAsync(ct);
        var status = latestPresence is null
            ? PresenceStatus.Offline
            : PresenceStatus.Overall(
                latestPresence.DesktopStatusAfter, latestPresence.MobileStatusAfter, latestPresence.WebStatusAfter);

        var nameHistory = await db.UserNameHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.ChangedAtUtc)
            .Select(h => new NameChangeDto(
                h.UsernameBefore, h.UsernameAfter, h.GlobalNameBefore, h.GlobalNameAfter, h.ChangedAtUtc))
            .ToListAsync(ct);

        return new ProfileDto(
            user.DiscordId, user.Username, user.GlobalName, user.AvatarHash, user.IsBot,
            status, user.FirstSeenUtc,
            messageCount, memeCount, reactionsReceived, voiceMinutes, onlineMinutes,
            favoriteEmoteDto, busiestChannelDto, messagesDaily14, nameHistory);
    }

    // Voice minutes: each event where the user is in a channel
    // (ChannelDiscordIdAfter not null) contributes the gap until their next event.
    // Per-user rows are small, so sessionize in memory.
    private async Task<long> VoiceMinutesAsync(ulong discordSf, CancellationToken ct)
    {
        var events = await db.VoiceStateEvents.AsNoTracking()
            .Where(v => v.UserDiscordId == discordSf)
            .OrderBy(v => v.ReceivedAtUtc)
            .Select(v => new { v.ReceivedAtUtc, v.ChannelDiscordIdAfter })
            .ToListAsync(ct);

        var downtimes = await db.BotDowntimeIntervals.AsNoTracking()
            .Select(d => new { d.StartedAtUtc, d.EndedAtUtc })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var totalMinutes = 0d;
        for (var i = 0; i < events.Count - 1; i++)
        {
            if (events[i].ChannelDiscordIdAfter is null)
            {
                continue;
            }

            var start = events[i].ReceivedAtUtc;
            var end = events[i + 1].ReceivedAtUtc;

            // Subtract any bot-downtime overlap: during an outage the user's "leave" can
            // go unobserved, so the raw gap would over-credit voice time. We clamp out the
            // blind windows rather than drop the whole segment (mirrors the SQL
            // VoiceSegmentMinutes and the OnlineMinutesAsync downtime handling).
            var downSeconds = downtimes.Sum(d =>
            {
                var overlapStart = start > d.StartedAtUtc ? start : d.StartedAtUtc;
                var dEnd = d.EndedAtUtc ?? now;
                var overlapEnd = end < dEnd ? end : dEnd;
                return overlapEnd > overlapStart ? (overlapEnd - overlapStart).TotalSeconds : 0d;
            });

            var seconds = (end - start).TotalSeconds - downSeconds;
            if (seconds > 0)
            {
                totalMinutes += seconds / 60.0;
            }
        }

        return (long)Math.Round(totalMinutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Online minutes (best-effort approximation). Discord does not expose presence
    /// durations, so we sessionize presence_events: each event's contribution is the gap
    /// until the next event, counted only when (a) the event's overall device status is
    /// non-offline (max device after-status &gt; 0), (b) the gap is at most 30 minutes — a
    /// cap that keeps stale/offline tails and bot-downtime windows from inflating the
    /// figure (longer-gap segments are dropped entirely, not clamped), and (c) the
    /// [start, next) segment does not overlap a recorded bot-downtime interval (open
    /// intervals coalesced to now()). Treat the result as an approximation, not a precise
    /// online-time ledger.
    /// </summary>
    private async Task<long> OnlineMinutesAsync(ulong discordSf, CancellationToken ct)
    {
        var events = await db.PresenceEvents.AsNoTracking()
            .Where(p => p.UserDiscordId == discordSf)
            .OrderBy(p => p.ReceivedAtUtc)
            .Select(p => new
            {
                p.ReceivedAtUtc,
                Overall = Math.Max(Math.Max(p.DesktopStatusAfter, p.MobileStatusAfter), p.WebStatusAfter),
            })
            .ToListAsync(ct);

        var downtimes = await db.BotDowntimeIntervals.AsNoTracking()
            .Select(d => new { d.StartedAtUtc, d.EndedAtUtc })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var totalMinutes = 0d;
        for (var i = 0; i < events.Count - 1; i++)
        {
            var current = events[i];
            var next = events[i + 1];
            if (current.Overall <= 0)
            {
                continue;
            }

            var gap = next.ReceivedAtUtc - current.ReceivedAtUtc;
            if (gap > TimeSpan.FromMinutes(30))
            {
                continue;
            }

            var overlapsDowntime = downtimes.Any(d =>
                current.ReceivedAtUtc < (d.EndedAtUtc ?? now) && next.ReceivedAtUtc > d.StartedAtUtc);
            if (overlapsDowntime)
            {
                continue;
            }

            totalMinutes += gap.TotalMinutes;
        }

        return (long)Math.Round(totalMinutes, MidpointRounding.AwayFromZero);
    }

    // Last 14 CET days of the user's messages, zero-filled oldest -> newest. The day
    // series is anchored on "today" in guild-local time; each message timestamp is
    // converted to that zone and bucketed by its local calendar day.
    private async Task<IReadOnlyList<ProfileDailyPointDto>> MessagesDaily14Async(Guid userId, CancellationToken ct)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Tz);
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        var startLocal = todayLocal.AddDays(-13);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), tz);

        var timestamps = await db.Messages.AsNoTracking()
            .Where(m => m.AuthorId == userId && m.CreatedAtUtc >= startUtc)
            .Select(m => m.CreatedAtUtc)
            .ToListAsync(ct);

        var counts = timestamps
            .GroupBy(ts => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ts, DateTimeKind.Utc), tz).Date)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var series = new List<ProfileDailyPointDto>(14);
        for (var i = 0; i < 14; i++)
        {
            var day = startLocal.AddDays(i);
            counts.TryGetValue(day, out var count);
            series.Add(new ProfileDailyPointDto(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), count));
        }

        return series;
    }
}
