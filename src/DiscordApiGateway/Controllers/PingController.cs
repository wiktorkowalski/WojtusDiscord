using Microsoft.AspNetCore.Mvc;

namespace DiscordApiGateway.Controllers;

[ApiController]
[Route("[controller]")]
public class PingController : ControllerBase
{
    [HttpGet("ping", Name = "Ping")]
    public IActionResult Ping()
    {
        return Ok(new { result = "DiscordApiGateway is running" });
    }
}
