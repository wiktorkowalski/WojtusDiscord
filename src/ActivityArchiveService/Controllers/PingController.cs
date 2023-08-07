using Microsoft.AspNetCore.Mvc;

namespace ActivityArchiveService.Controllers;

[ApiController]
[Route("[controller]")]
public class PingController : ControllerBase
{
    [HttpGet("ping", Name = "Ping")]
    public IActionResult Ping()
    {
        return Ok(new { result = "ActivityArchiveService is running" });
    }
}