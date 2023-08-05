using Microsoft.AspNetCore.Mvc;

namespace ActivityListenerService.Controllers;

[ApiController]
[Route("[controller]")]
public class PingController : ControllerBase
{
    [HttpGet("ping", Name = "Ping")]
    public IActionResult Ping()
    {
        return Ok(new { result = "ActivityListenerService is running" });
    }
}
