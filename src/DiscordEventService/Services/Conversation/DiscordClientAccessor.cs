using DSharpPlus;

namespace DiscordEventService.Services.Conversation;

// Hands the singleton DiscordClient to the §6 action services WITHOUT registering DiscordClient
// in the DSharpPlus child container. Forwarding DiscordClient into the container it itself owns
// (`services.AddSingleton(_ => rootSp.GetRequiredService<DiscordClient>())`) is a re-entrant edge:
// ValidateOnBuild constructs the root DiscordClient (clientBuilder.Build builds that child
// container), and resolving the forwarded DiscordClient back out of it during build deadlocks the
// host before any log line — a hang VALIDATE_AND_EXIT can race past but a real boot does not.
//
// This holder has no dependencies, so resolving it never touches DiscordClient. Program.cs sets
// Client once, right after the DiscordClient singleton is built and before app.Run(), so it is
// always populated by the time any handler or tool reads it.
internal sealed class DiscordClientAccessor
{
    private DiscordClient? _client;

    public DiscordClient Client
    {
        get => _client ?? throw new InvalidOperationException(
            "DiscordClientAccessor.Client was read before it was set during startup.");
        set => _client = value;
    }
}
