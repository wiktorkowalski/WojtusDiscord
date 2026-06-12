using DSharpPlus;

namespace DiscordEventService.Services;

internal interface IDiscordEventHandler
{
    void RegisterHandlers(DiscordClient client);
}
