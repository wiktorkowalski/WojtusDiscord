using System.Runtime.CompilerServices;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordEventService.Tests;

// Hand-written fakes for the Discord-free interaction port (#308). The whole point of the
// port is that these are the only thing standing between a unit test and the real flows —
// no Discord client, no chat client, no DbContext, no mocking framework.

// The events of a turn through the real TurnRenderer, as the messages Discord receives.
internal static class TurnRendering
{
    public static async Task<List<string>> RenderAsync(IEnumerable<ConversationUpdate> events)
    {
        var surface = new FakeTurnSurface();
        var renderer = new TurnRenderer(surface, NullLogger.Instance);
        foreach (var update in events)
            await renderer.ApplyAsync(update);
        await renderer.CompleteTurnAsync();
        return surface.Sent;
    }

    // The last message of the turn: the answer.
    public static async Task<string> FinalAnswerAsync(IEnumerable<ConversationUpdate> events) =>
        (await RenderAsync(events)).LastOrDefault() ?? string.Empty;
}

internal sealed class FakeTurnSurface(ulong channelId = 100UL) : ITurnSurface
{
    public ulong ChannelId { get; } = channelId;

    public List<string> Sent { get; } = [];

    public int TypingCount { get; private set; }

    // Thrown by every typing POST.
    public Exception? TypingFailure { get; set; }

    // Thrown by the send of a message equal to this text (the message is not recorded).
    public string? FailingSend { get; set; }

    public Task SendAsync(string content)
    {
        if (content == FailingSend)
            throw new InvalidOperationException("Discord 500");
        Sent.Add(content);
        return Task.CompletedTask;
    }

    public Task TriggerTypingAsync()
    {
        TypingCount++;
        return TypingFailure is null ? Task.CompletedTask : Task.FromException(TypingFailure);
    }
}

internal sealed class FakeConversationGateway(FakeTurnSurface origin) : IConversationGateway
{
    public ITurnSurface Origin { get; } = origin;

    public string? CreatedThreadName { get; private set; }

    public FakeTurnSurface? CreatedThread { get; private set; }

    public Exception? CreateThreadFailure { get; set; }

    public Task<ITurnSurface> CreateThreadAsync(string name)
    {
        if (CreateThreadFailure is not null)
            return Task.FromException<ITurnSurface>(CreateThreadFailure);
        CreatedThreadName = name;
        CreatedThread = new FakeTurnSurface(999UL);
        return Task.FromResult<ITurnSurface>(CreatedThread);
    }
}

// Scripted stand-in for ConversationService: records the turn it was asked for and
// replays a fixed sequence of render events.
internal sealed class FakeTurnSource : IConversationTurnSource
{
    public bool IsConfigured { get; set; } = true;

    // The channels the store holds a conversation for.
    public HashSet<ulong> Conversations { get; } = [];

    // Thrown by every store lookup.
    public Exception? StoreFailure { get; set; }

    public int StoreLookups { get; private set; }

    public List<(string? Message, ConversationContext Context)> Turns { get; } = [];

    public Task<bool> HasConversationAsync(ulong channelId, CancellationToken cancellationToken)
    {
        StoreLookups++;
        return StoreFailure is not null
            ? Task.FromException<bool>(StoreFailure)
            : Task.FromResult(Conversations.Contains(channelId));
    }

    public List<ConversationUpdate> Updates { get; } = [];

    // Thrown after Updates have been yielded.
    public Exception? Failure { get; set; }

    // Hangs after Updates have been yielded, so the flow's own timeout fires.
    public bool HangUntilCancelled { get; set; }

    // When set, the first turn waits for it after Updates — a turn still running (#449).
    public TaskCompletionSource? HoldFirstTurn { get; set; }

    public async IAsyncEnumerable<ConversationUpdate> GenerateReplyAsync(
        string? userMessage, ConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Turns.Add((userMessage, context));

        foreach (var update in Updates)
            yield return update;

        if (HangUntilCancelled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        if (HoldFirstTurn is not null && Turns.Count == 1)
            await HoldFirstTurn.Task.WaitAsync(cancellationToken);

        if (Failure is not null)
            throw Failure;
    }
}

internal sealed class FakeUsageAlertService : IUsageAlertService
{
    public List<ulong> Checked { get; } = [];

    public Task CheckAndAlertAsync(ulong invokerId)
    {
        Checked.Add(invokerId);
        return Task.CompletedTask;
    }
}

// An in-memory stand-in for the staged-action store with the same claim-once semantics.
// The trace list is shared with the surface fake (and the staged action itself), so a test
// can assert the ack-before-claim-before-execute ordering as one sequence.
internal sealed class FakeStagedActionStore(List<string> trace) : IConfirmationService
{
    private readonly Dictionary<string, PendingGuildAction> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _handled = new(StringComparer.Ordinal);

    public void Stage(string token, PendingGuildAction action) => _pending[token] = action;

    public bool Contains(string token) => _pending.ContainsKey(token);

    public Task<string> StageAsync(
        ulong channelId, ulong requesterId, string requesterName, string description,
        Func<CancellationToken, Task<string>> execute, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The flow never stages — it only claims.");

    public bool TryClaim(string token, out PendingGuildAction action)
    {
        trace.Add("claim");
        if (!_pending.Remove(token, out action!))
            return false;
        _handled.Add(token);
        return true;
    }

    public bool WasHandled(string token) => _handled.Contains(token);
}

internal sealed class FakeConfirmationSurface(List<string> trace) : IConfirmationSurface
{
    public Exception? AcknowledgeFailure { get; set; }

    public Exception? EditFailure { get; set; }

    public Exception? FallbackFailure { get; set; }

    public Exception? ReplaceFailure { get; set; }

    public Task RespondEphemeralAsync(string content)
    {
        trace.Add($"ephemeral:{content}");
        return Task.CompletedTask;
    }

    public Task AcknowledgeAsync()
    {
        trace.Add("ack");
        return AcknowledgeFailure is not null ? Task.FromException(AcknowledgeFailure) : Task.CompletedTask;
    }

    public Task FollowupEphemeralAsync(string content)
    {
        trace.Add($"followup:{content}");
        return Task.CompletedTask;
    }

    public Task EditPromptAsync(string content)
    {
        trace.Add($"edit:{content}");
        return EditFailure is not null ? Task.FromException(EditFailure) : Task.CompletedTask;
    }

    public Task ReplacePromptAsync(string content)
    {
        trace.Add($"replace:{content}");
        return ReplaceFailure is not null ? Task.FromException(ReplaceFailure) : Task.CompletedTask;
    }

    public Task SendChannelFallbackAsync(string content)
    {
        trace.Add($"fallback:{content}");
        return FallbackFailure is not null ? Task.FromException(FallbackFailure) : Task.CompletedTask;
    }
}
