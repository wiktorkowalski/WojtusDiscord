using System.Globalization;
using System.Text;
using System.Text.Json;
using DiscordEventService.Data;
using DiscordEventService.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Controllers;

/// <summary>
/// Unified, time-ordered activity feed over raw_event_logs (the single append-only
/// source of every gateway event). Keyset pagination on (received_at_utc DESC, id)
/// — both indexed — so deep pages stay fast and never skip/duplicate rows.
/// </summary>
[ApiController]
[Route("api/timeline")]
public sealed class TimelineController(DiscordDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    [HttpGet]
    public async Task<ActionResult<TimelinePage>> GetTimeline(
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? cursor = null,
        [FromQuery] string? eventType = null,
        [FromQuery] ulong? userId = null,
        [FromQuery] ulong? channelId = null,
        [FromQuery] DateTime? after = null,
        [FromQuery] DateTime? before = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.RawEventLogs.AsNoTracking();

        if (after is not null)
        {
            query = query.Where(r => r.ReceivedAtUtc >= after.Value);
        }
        if (before is not null)
        {
            query = query.Where(r => r.ReceivedAtUtc <= before.Value);
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var types = eventType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(r => types.Contains(r.EventType));
        }
        if (userId is not null)
        {
            query = query.Where(r => r.UserDiscordId == userId.Value);
        }
        if (channelId is not null)
        {
            query = query.Where(r => r.ChannelDiscordId == channelId.Value);
        }

        if (TryDecodeCursor(cursor, out var cursorTs, out var cursorId))
        {
            // Keyset predicate for (received_at_utc DESC, id DESC).
            query = query.Where(r =>
                r.ReceivedAtUtc < cursorTs || (r.ReceivedAtUtc == cursorTs && r.Id < cursorId));
        }

        // Fetch one extra row to detect whether a further page exists.
        var rows = await query
            .OrderByDescending(r => r.ReceivedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(pageSize + 1)
            .Select(r => new
            {
                r.Id,
                r.EventType,
                r.GuildDiscordId,
                r.ChannelDiscordId,
                r.UserDiscordId,
                r.ReceivedAtUtc,
                r.JsonSizeBytes,
                r.SerializationFailed,
                r.EventJson,
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var events = rows.Select(r => new TimelineEventDto(
            r.Id,
            r.EventType,
            r.GuildDiscordId,
            r.ChannelDiscordId,
            r.UserDiscordId,
            r.ReceivedAtUtc,
            r.JsonSizeBytes,
            r.SerializationFailed,
            ParsePayload(r.EventJson))).ToList();

        var nextCursor = hasMore && events.Count > 0
            ? EncodeCursor(events[^1].ReceivedAtUtc, events[^1].Id)
            : null;

        return Ok(new TimelinePage(events, nextCursor, hasMore));
    }

    private static JsonElement ParsePayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Diagnostic stubs / malformed payloads: surface the raw text rather than 500.
            return JsonSerializer.SerializeToElement(new { raw = json });
        }
    }

    private static string EncodeCursor(DateTime receivedAtUtc, Guid id)
    {
        var raw = $"{receivedAtUtc.ToUniversalTime():O}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static bool TryDecodeCursor(string? cursor, out DateTime receivedAtUtc, out Guid id)
    {
        receivedAtUtc = default;
        id = default;
        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|', 2);
            if (parts.Length != 2)
            {
                return false;
            }
            receivedAtUtc = DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return Guid.TryParse(parts[1], out id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
