using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace WojtusDiscord.ActivityArchiveService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            using var activity = new ActivitySource(this.GetType().Name).StartActivity();
            return Ok(new { status = "ActivityArchiveService is running" });
        }
    }
}
