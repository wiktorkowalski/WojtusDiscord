using System;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WojtusDiscord.API.Models.Health;
using WojtusDiscord.API.Services.Bot;

namespace WojtusDiscord.API.Services.PubSub
{
    public class RedisPubSubService : IPubSubService
    {
        private readonly ILogger<RedisPubSubService> _logger;
        private readonly IBotCommunicationService _botCommunicationService;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;

        public RedisPubSubService(ILogger<RedisPubSubService> logger, IBotCommunicationService botCommunicationService)
        {
            _logger = logger;
            _botCommunicationService = botCommunicationService;
            _connectionMultiplexer = ConnectionMultiplexer.Connect("redis:6379");
            _database = _connectionMultiplexer.GetDatabase();
            _logger.LogInformation($"Connected to Redis database {_database.Database}");
            var subscriber = _database.Multiplexer.GetSubscriber();
            subscriber.Subscribe("RegisterBot").OnMessage(chanelMessage =>
            {
                _logger.LogInformation($"Message on {chanelMessage.Channel}: {chanelMessage.Message}");
                _botCommunicationService.RegisterBotInstance(Guid.Parse(chanelMessage.Message));
            });
            subscriber.Publish("RegisterBotPrompt", RedisValue.EmptyString);
        }


        public PubSubServiceHealthCheck HealthCheck()
        {
            _logger.LogInformation($"Health Check in {nameof(RedisPubSubService)}");
            return new PubSubServiceHealthCheck
            {
                Database = _database.Database,
                IsConnected = _database.Multiplexer.IsConnected,
                Status = _database.Multiplexer.GetStatus()
            };
        }
    }
}
