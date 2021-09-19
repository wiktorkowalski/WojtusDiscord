using System.Net;
using System;

namespace WojtusDiscord.API.Models.Health
{
    internal class GetHealthDetailsResponseModel
    {
        public DateTime Timestamp { get; set; }
        public HttpStatusCode HTTPStatus { get; set; }
        public bool IsDetailed => true;
    }
}
