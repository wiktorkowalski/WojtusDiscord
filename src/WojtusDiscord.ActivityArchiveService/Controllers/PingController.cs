using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WojtusDiscord.ActivityArchiveService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PingController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("Pong");
        }
    }
}
