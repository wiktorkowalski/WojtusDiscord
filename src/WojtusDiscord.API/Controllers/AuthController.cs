using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WojtusDiscord.API.Models.Auth;
using WojtusDiscord.API.Services.Auth;

namespace WojtusDiscord.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ITokenService tokenService;

        public AuthController(ITokenService tokenService)
        {
            this.tokenService = tokenService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public IActionResult Login([FromBody] LoginModel loginData)
        {
            // todo: store accounts in database
            if (loginData.Username == "admin" && loginData.Password == "admin")
            {
                return Ok(tokenService.BuildToken(loginData));
            }

            return BadRequest("Failed to authenticate");
        }
    }
}
