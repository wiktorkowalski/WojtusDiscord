using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using WojtusDiscord.API.Models.Health;

namespace WojtusDiscord.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        [AllowAnonymous]
        public IActionResult GetHealth()
        {
            return Ok(new GetHealthResponseModel
            {
                HTTPStatus = HttpStatusCode.OK,
                Timestamp = DateTime.Now
            });
        }

        [HttpGet("details")]
        public IActionResult GetHealthDetails()
        {
            return Ok(new GetHealthDetailsResponseModel
            {
                HTTPStatus = HttpStatusCode.OK,
                Timestamp = DateTime.Now
            });
        }
    }
}
