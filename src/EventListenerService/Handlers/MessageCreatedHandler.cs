using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace EventListenerService.Handlers;

[GatewayEvent(nameof(GatewayClient.MessageCreate))]
public class MessageCreateHandler(ILogger<MessageCreateHandler> logger) : IGatewayEventHandler<Message>
{
    public ValueTask HandleAsync(Message message)
    {
        logger.LogInformation("{}", message);
        return default;
    }
}