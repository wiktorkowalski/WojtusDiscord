using Microsoft.AspNetCore.Mvc;

namespace DiscordApiGateway.Controllers;

[ApiController]
[Route("[controller]")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Ping()
    {
        return Ok(new { result = "DiscordApiGateway is running" });
    }
    
    [HttpGet("detailed")]
    public IActionResult DetailedPing()
    {
        return Ok(new
        {
            result = "DiscordApiGateway is running",
            podName = Environment.GetEnvironmentVariable("POD_NAME"),
            instanceId = AppInfo.InstanceId,
        });
    }
}
