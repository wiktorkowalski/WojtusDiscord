using System;
using System.Collections.Generic;

namespace WojtusDiscord.API.Services.Bot
{
    public interface IBotCommunicationService
    {
        public void RegisterBotInstance(Guid botGuid);
        public IEnumerable<Guid> GetConnectedBotInstances();
    }
}
