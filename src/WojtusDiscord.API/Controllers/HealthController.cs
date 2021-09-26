using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using WojtusDiscord.API.Models.Health;
using WojtusDiscord.API.Services.Bot;
using WojtusDiscord.API.Services.PubSub;

namespace WojtusDiscord.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class HealthController : ControllerBase
    {
        private readonly ILogger<HealthController> _logger;
        private readonly IPubSubService _pubSubService;
        private readonly IBotCommunicationService _botCommunicationService;

        public HealthController(ILogger<HealthController> logger, IPubSubService pubSubService, IBotCommunicationService botCommunicationService)
        {
            _logger = logger;
            _pubSubService = pubSubService;
            _botCommunicationService = botCommunicationService;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult GetHealth()
        {
            _logger.LogInformation($"Health Check at {nameof(HealthController)}");

            return Ok(new GetHealthResponseModel
            {
                HTTPStatus = HttpStatusCode.OK,
                Timestamp = DateTime.Now
            });
        }

        [HttpGet("details")]
        [AllowAnonymous]
        public IActionResult GetHealthDetails()
        {
            _logger.LogInformation($"Health Check at {nameof(HealthController)}");

            return Ok(new GetHealthDetailsResponseModel
            {
                HTTPStatus = HttpStatusCode.OK,
                Timestamp = DateTime.Now,
                PubSubStatus = _pubSubService.HealthCheck(),
                ConnectedBotInstances = _botCommunicationService.GetConnectedBotInstances()
            });
        }
    }
}
