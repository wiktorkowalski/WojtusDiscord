using System.Runtime.CompilerServices;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.Conversation.Interaction;

namespace DiscordEventService.Tests;

// Hand-written fakes for the Discord-free interaction port (#308). The whole point of the
// port is that these are the only thing standing between a unit test and the real flows —
// no Discord client, no chat client, no DbContext, no mocking framework.

internal sealed class FakeTurnSurface(ulong channelId = 100UL) : ITurnSurface
{
    public ulong ChannelId { get; } = channelId;

    public List<string> Sent { get; } = [];

    public int TypingCount { get; private set; }

    public Task SendAsync(string content)
    {
        Sent.Add(content);
        return Task.CompletedTask;
    }

    public Task TriggerTypingAsync()
    {
        TypingCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeConversationGateway(FakeTurnSurface origin) : IConversationGateway
{
    public ITurnSurface Origin { get; } = origin;

    // What ResolveThreadCreatorAsync answers (null = creator unknown).
    public ulong? ThreadCreatorId { get; set; }

    public Exception? ResolveFailure { get; set; }

    public int ResolveCalls { get; private set; }

    public string? CreatedThreadName { get; private set; }

    public FakeTurnSurface? CreatedThread { get; private set; }

    public Task<ulong?> ResolveThreadCreatorAsync()
    {
        ResolveCalls++;
        return ResolveFailure is not null
            ? Task.FromException<ulong?>(ResolveFailure)
            : Task.FromResult(ThreadCreatorId);
    }

    public Task<ITurnSurface> CreateThreadAsync(string name)
    {
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

    public List<(string? Message, ConversationContext Context)> Turns { get; } = [];

    public List<ConversationUpdate> Updates { get; } = [];

    // Thrown after Updates have been yielded.
    public Exception? Failure { get; set; }

    // Hangs after Updates have been yielded, so the flow's own timeout fires.
    public bool HangUntilCancelled { get; set; }

    public async IAsyncEnumerable<ConversationUpdate> GenerateReplyAsync(
        string? userMessage, ConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Turns.Add((userMessage, context));

        foreach (var update in Updates)
            yield return update;

        if (HangUntilCancelled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

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

    public void Stage(string token, PendingGuildAction action) => _pending[token] = action;

    public bool Contains(string token) => _pending.ContainsKey(token);

    public Task<string> StageAsync(
        ulong channelId, ulong requesterId, string requesterName, string description,
        Func<CancellationToken, Task<string>> execute, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The flow never stages — it only claims.");

    public bool TryClaim(string token, out PendingGuildAction action)
    {
        trace.Add("claim");
        return _pending.Remove(token, out action!);
    }
}

internal sealed class FakeConfirmationSurface(List<string> trace) : IConfirmationSurface
{
    public Exception? AcknowledgeFailure { get; set; }

    public Exception? EditFailure { get; set; }

    public Exception? FallbackFailure { get; set; }

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
        return Task.CompletedTask;
    }

    public Task SendChannelFallbackAsync(string content)
    {
        trace.Add($"fallback:{content}");
        return FallbackFailure is not null ? Task.FromException(FallbackFailure) : Task.CompletedTask;
    }
}
