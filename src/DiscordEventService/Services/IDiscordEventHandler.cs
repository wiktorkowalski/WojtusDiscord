using DSharpPlus;

namespace DiscordEventService.Services;

public interface IDiscordEventHandler
{
    void RegisterHandlers(DiscordClient client);
}
