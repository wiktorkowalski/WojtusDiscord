using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace WojtusDiscord.API.Services.Bot
{
    public class BotCommunicationService : IBotCommunicationService
    {
        private readonly ILogger<BotCommunicationService> _logger;
        private readonly HashSet<Guid> _connectedBotInstances = new HashSet<Guid>();

        public BotCommunicationService(ILogger<BotCommunicationService> logger)
        {
            _logger = logger;
        }

        public void RegisterBotInstance(Guid botGuid)
        {
            _connectedBotInstances.Add(botGuid);
        }

        public IEnumerable<Guid> GetConnectedBotInstances()
        {
            return _connectedBotInstances.AsEnumerable();
        }
    }
}