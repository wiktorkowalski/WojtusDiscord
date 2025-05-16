using Microsoft.AspNetCore.Mvc;

namespace EventListenerService.Controllers;

[ApiController]
[Route("[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { message = "EventListenerService is running" });
    }
}
