using WojtusDiscord.API.Models.Health;

namespace WojtusDiscord.API.Services.PubSub
{
    public interface IPubSubService
    {
        public PubSubServiceHealthCheck HealthCheck();
    }
}