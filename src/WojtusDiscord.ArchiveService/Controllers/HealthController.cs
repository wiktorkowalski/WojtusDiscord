using Microsoft.AspNetCore.Mvc;

namespace WojtusDiscord.ArchiveService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { ServiceName = "WojtusDiscord.ArchiveService" });
        }
    }
}
