namespace WojtusDiscord.API.Models.Health
{
    public class PubSubServiceHealthCheck
    {
        public int Database { get; set; }
        public bool IsConnected { get; set; }
        public string Status { get; set; }
    }
}