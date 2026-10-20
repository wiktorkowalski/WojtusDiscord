using System.Collections.Concurrent;

namespace DiscordEventService.Services.Conversation.Interaction;

// One conversation turn at a time per channel (#449). DSharpPlus dispatches events
// fire-and-forget and ConversationFlow is scoped per event, so two quick messages in one
// thread would otherwise run two turns at once: double spend, interleaved replies, and a
// shared TurnIndex (ConversationMemoryService reads MAX(turn_index) unlocked). Singleton.
internal sealed class ConversationTurnGate
{
    // shortcut: one semaphore per channel is never removed; fine for a few thousand
    // conversation channels, add eviction if that count ever matters.
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new();

    public async Task<IDisposable> EnterAsync(ulong channelId)
    {
        var gate = _gates.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
