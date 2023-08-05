using System.Text.Json;
using ActivityListenerService.Models;
using RabbitMQ.Client;

namespace ActivityListenerService.Services;

public class ActivityPublisherService : IMessagePublisherService
{
    private readonly ILogger<ActivityPublisherService> _logger;
    private readonly IModel _channel;
    private readonly IConnection _connection;

    public ActivityPublisherService(ILogger<ActivityPublisherService> _logger)
    {
        this._logger = _logger;
        var connectionString = "amqp://guest:guest@localhost:5672/";
        var factory = new ConnectionFactory { HostName = "localhost", UserName = "guest", Password = "guest"};
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(exchange: "discord.activity", type: ExchangeType.Fanout);
    }
    
    public async Task PublishActivityAsync<T>(PublishedMessagePayload<T> payload)
    {
        _channel.BasicPublish(exchange: "discord.activity",
            routingKey: "discord.activity",
            basicProperties: null,
            body: JsonSerializer.SerializeToUtf8Bytes(payload));
    }
}

public interface IMessagePublisherService
{
    public Task PublishActivityAsync<T>(PublishedMessagePayload<T> message);
}