using Microsoft.AspNetCore.Mvc;

namespace WojtusDiscord.ActivityArchiveService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok(new { status = "ActivityArchiveService is running" });
        }
    }
}
