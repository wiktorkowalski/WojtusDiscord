using System.Net;
using System;
using System.Collections.Generic;

namespace WojtusDiscord.API.Models.Health
{
    internal class GetHealthDetailsResponseModel
    {
        public DateTime Timestamp { get; set; }
        public HttpStatusCode HTTPStatus { get; set; }
        public bool IsDetailed => true;
        public PubSubServiceHealthCheck PubSubStatus { get; set; }
        public IEnumerable<Guid> ConnectedBotInstances { get; set; }
    }
}
