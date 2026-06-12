using DiscordEventService.Data;
using DiscordEventService.Dtos;
using DiscordEventService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscordEventService.Controllers;

[ApiController]
[Route("api/raw-events")]
public sealed class RawEventsController(DiscordDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    [HttpGet("types")]
    public async Task<ActionResult<IReadOnlyList<RawEventTypeDto>>> GetTypes(CancellationToken ct)
    {
        var grouped = await db.RawEventLogs.AsNoTracking()
            .GroupBy(r => r.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync(ct);

        var types = grouped.Select(g => new RawEventTypeDto(g.EventType, g.Count)).ToList();
        return Ok(types);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<RawEventSummaryDto>>> GetEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? eventType = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] bool failedOnly = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.RawEventLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(r => r.EventType == eventType);
        if (since is not null)
        {
            var sinceUtc = since.Value.ToUtcInstant();
            query = query.Where(r => r.ReceivedAtUtc >= sinceUtc);
        }
        if (failedOnly)
            query = query.Where(r => r.SerializationFailed);

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.ReceivedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RawEventSummaryDto(
                r.Id, r.EventType, r.GuildDiscordId, r.ChannelDiscordId, r.UserDiscordId,
                r.ReceivedAtUtc, r.JsonSizeBytes, r.SerializationFailed))
            .ToListAsync(ct);

        return Ok(new PagedResult<RawEventSummaryDto>(items, total, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RawEventDetailDto>> GetEvent(Guid id, CancellationToken ct)
    {
        var row = await db.RawEventLogs.AsNoTracking()
            .Where(r => r.Id == id)
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
                r.CorrelationId,
                r.EventJson,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return NotFound();

        return Ok(new RawEventDetailDto(
            row.Id, row.EventType, row.GuildDiscordId, row.ChannelDiscordId, row.UserDiscordId,
            row.ReceivedAtUtc, row.JsonSizeBytes, row.SerializationFailed, row.CorrelationId,
            JsonPayload.Parse(row.EventJson)));
    }
}
