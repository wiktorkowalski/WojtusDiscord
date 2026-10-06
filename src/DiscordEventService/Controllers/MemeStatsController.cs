using DiscordEventService.Dtos;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.AspNetCore.Mvc;

namespace DiscordEventService.Controllers;

// The dashboard's "Meme index" page (#395). Every action is a read. The queries are in
// MemeStatsReader; this class checks the input.
[ApiController]
[Route(RoutePrefix)]
public sealed class MemeStatsController(IMemeStatsReader stats) : ControllerBase
{
    // MemeStatsReader builds the thumbnail links of its answers from these two.
    public const string RoutePrefix = "api/stats/memes";
    public const string ThumbnailSegment = "thumbnails";

    public const int DefaultUsageDays = 30;
    public const int MaxSearchLimit = 20;
    public const int MaxQueryLength = 200;

    // The windows the page can ask for. A closed list: each value is one cached answer, so a
    // caller cannot make every request a new computation by changing the number.
    public static readonly IReadOnlyList<int> AllowedUsageDays = [7, DefaultUsageDays, 90, 365];

    // Retry-After of a 429 or a 503 from a cap of MemeDashboardLimits.
    private const string RetryAfterSeconds = "5";

    [HttpGet]
    [ProducesResponseType<MemeIndexDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemeIndexDto>> Index(CancellationToken ct) =>
        await stats.GetIndexAsync(ct) is { } index ? index : Busy();

    [HttpGet("search-usage")]
    [ProducesResponseType<MemeSearchUsageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemeSearchUsageDto>> SearchUsage(
        [FromQuery] int days = DefaultUsageDays, CancellationToken ct = default)
    {
        if (!AllowedUsageDays.Contains(days))
            return BadRequest(new { error = $"days must be one of: {string.Join(", ", AllowedUsageDays)}." });

        return await stats.GetSearchUsageAsync(days, ct) is { } usage ? usage : Busy();
    }

    [HttpGet("search")]
    [ProducesResponseType<MemeSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MemeSearchResultDto>> Search(
        [FromQuery] string? q, [FromQuery] int limit = MemeSearchService.DefaultLimit, CancellationToken ct = default)
    {
        var query = q?.Trim();
        if (string.IsNullOrEmpty(query))
            return BadRequest(new { error = "q must not be empty." });
        if (query.Length > MaxQueryLength)
            return BadRequest(new { error = $"q must be at most {MaxQueryLength} characters." });
        if (limit is < 1 or > MaxSearchLimit)
            return BadRequest(new { error = $"limit must be between 1 and {MaxSearchLimit}." });

        // One answer for both caps of the tester (at one time, per minute): the caller does not
        // learn which one it reached.
        var result = await stats.SearchAsync(query, limit, ct);
        if (result is null)
        {
            Response.Headers.RetryAfter = RetryAfterSeconds;
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many searches right now. Try again." });
        }

        return result;
    }

    // 302 to a freshly signed Discord CDN URL, for an indexed meme of a current meme channel
    // only. The rules and the caps are in MemeThumbnailResolver. 404 does not say why.
    [HttpGet(ThumbnailSegment + "/{attachmentDiscordId}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Thumbnail(
        ulong attachmentDiscordId, [FromServices] IMemeThumbnailResolver thumbnails, CancellationToken ct)
    {
        var thumbnail = await thumbnails.ResolveAsync(attachmentDiscordId, ct);
        if (thumbnail.Url is null && thumbnail.IsRetryable)
        {
            Response.Headers.RetryAfter = RetryAfterSeconds;
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (thumbnail.Url is null)
            return NotFound();

        Response.Headers.CacheControl = $"private, max-age={(int)MemeThumbnailResolver.BrowserMaxAge.TotalSeconds}";
        return Redirect(thumbnail.Url);
    }

    // The answer is being computed for another request and the wait for it reached its limit.
    private ObjectResult Busy()
    {
        Response.Headers.RetryAfter = RetryAfterSeconds;
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The answer is being computed. Try again." });
    }
}
