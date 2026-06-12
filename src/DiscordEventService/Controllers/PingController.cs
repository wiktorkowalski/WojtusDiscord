using Microsoft.AspNetCore.Mvc;

namespace DiscordEventService.Controllers;

[ApiController]
[Route("api/ping")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new PingResponse("pong"));
}

public sealed record PingResponse(string Message);
