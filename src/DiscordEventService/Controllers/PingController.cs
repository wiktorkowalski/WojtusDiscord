using Microsoft.AspNetCore.Mvc;

namespace DiscordEventService.Controllers;

/// <summary>
/// Liveness probe for the dashboard API surface. Confirms MVC controllers are
/// wired and the SPA's <c>/api</c> calls reach the backend end-to-end.
/// </summary>
[ApiController]
[Route("api/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new PingResponse("pong"));
}

public sealed record PingResponse(string Message);
