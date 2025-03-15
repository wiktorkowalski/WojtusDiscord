using System.Text.Json;
using ActivityListenerService.Models;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ActivityListenerService.Services;

public class ActivityPublisherService : IMessagePublisherService
{
    private readonly ILogger<ActivityPublisherService> _logger;
    private readonly IOptionsSnapshot<RabbitMqOptions> _options;
    private readonly IModel _channel;

    public ActivityPublisherService(ILogger<ActivityPublisherService> logger, IOptionsSnapshot<RabbitMqOptions> options)
    {
        _logger = logger;
        _options = options;
        var redisOptions = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = redisOptions.HostName,
            UserName = redisOptions.UserName,
            Password = redisOptions.Password
        };
        
        _channel = factory.CreateConnection().CreateModel();
        _channel.ExchangeDeclare(exchange: redisOptions.ActivityExchangeName, type: ExchangeType.Fanout);
    }
    
    public void PublishActivityAsync<T>(PublishedMessagePayload<T> payload)
    {
        _channel.BasicPublish(exchange: _options.Value.ActivityExchangeName,
            routingKey: _options.Value.ActivityRoutingKey,
            basicProperties: null,
            body: JsonSerializer.SerializeToUtf8Bytes(payload));

        _logger.LogInformation("Published Message");
    }
}

public interface IMessagePublisherService
{
    public void PublishActivityAsync<T>(PublishedMessagePayload<T> message);
}