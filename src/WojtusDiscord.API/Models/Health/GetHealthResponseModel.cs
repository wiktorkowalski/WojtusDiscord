using System;
using System.Net;

namespace WojtusDiscord.API.Models.Health
{
    internal class GetHealthResponseModel
    {
        public DateTime Timestamp { get; set; }
        public HttpStatusCode HTTPStatus {  get; set; }
        public bool IsDetailed => false;
    }
}
