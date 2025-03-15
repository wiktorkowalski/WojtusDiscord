namespace ActivityListenerService.Services;

public class RabbitMqOptions
{
    public const string Prefix = "RabbitMq";
    public string HostName { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
    public string ActivityExchangeName { get; set; }
    public string ActivityRoutingKey { get; set; }
}