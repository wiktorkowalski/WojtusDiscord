using DiscordEventService.Services.Conversation.Interaction;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace DiscordEventService.Services.EventHandlers;

// The DSharpPlus adapter for the §6 confirm/cancel buttons: flatten the click and hand it
// to ConfirmationFlow, which owns the admin re-check and the ack/claim ordering (#308).
internal sealed class ConfirmationComponentHandler(ConfirmationFlow flow)
    : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    public Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e) =>
        flow.HandleAsync(
            new ConfirmationClick(e.Id, e.User.Id, e.User.Username, e.Guild?.Id),
            new DiscordConfirmationSurface(e),
            // The channel holding the card — where the outcome feedback turn (#310) posts.
            new DiscordTurnSurface(e.Channel));
}
