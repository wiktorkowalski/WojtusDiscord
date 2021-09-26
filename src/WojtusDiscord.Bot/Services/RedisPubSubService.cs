using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace WojtusDiscord.Bot.Services
{
    public class RedisPubSubService : IPubSubService
    {
        private readonly ILogger<RedisPubSubService> _logger;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;
        private readonly ISubscriber _subscriber;
        private readonly Guid _guid;

        public RedisPubSubService(ILogger<RedisPubSubService> logger)
        {
            _guid = Guid.NewGuid();
            _logger = logger;
            _connectionMultiplexer = ConnectionMultiplexer.Connect("redis:6379");
            _database = _connectionMultiplexer.GetDatabase();
            _logger.LogInformation($"Connected to Redis database {_database.Database}");
            _subscriber = _database.Multiplexer.GetSubscriber();
        }

        public Task RegisterBot()
        {
            _subscriber.Subscribe("RegisterBotPrompt").OnMessage(chanelMessage =>
            {
                _logger.LogInformation($"Message on {chanelMessage.Channel}: {chanelMessage.Message}");
                _subscriber.PublishAsync("RegisterBot", _guid.ToString());
            });

            _logger.LogInformation($"Publish BotRegister: {_guid}");
            return _subscriber.PublishAsync("RegisterBot", _guid.ToString());
        }
    }
}
