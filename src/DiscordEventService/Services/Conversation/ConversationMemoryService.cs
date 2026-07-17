using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// A turn's handle into the conversation store (#267): which conversation it belongs to,
// its monotonic turn index, and the token-windowed replay of everything stored so far.
internal sealed record ConversationTurnState(
    Guid ConversationId, int TurnIndex, IReadOnlyList<ChatMessage> Window);

// One model-call attempt's ledger row content (#267). §1 always writes Attempt=1 and
// Failed=false; the §2 retry policy adds attempt>1 and failed rows (which may still bill).
internal sealed record ConversationRoundUsage(
    int Round, int Attempt, string Model,
    int? PromptTokens, int? CompletionTokens,
    double? CostUsd, double? UpstreamInferenceCostUsd, int? WebSearchRequests,
    long LatencyMs, bool Failed);

// The conversation store + usage ledger (#267, ADR-0006 ¶15): durable per-conversation
// memory for the agentic loop. Append-only writes at the loop's append points (awaited,
// inside the turn — a crash leaves a consistent prefix) and a token-windowed rehydration
// that replays byte-identical wire messages (see ToWireText for the object?-Result trap).
// Scoped over the request DbContext like MemeSearchService; dual-registered via
// CoreServiceRegistration.CoreServiceTypes.
internal sealed class ConversationMemoryService(
    DiscordDbContext db,
    IOptions<ConversationOptions> conversationOptions,
    ILogger<ConversationMemoryService> logger)
{
    // Stored shape of one assistant tool call inside the tool_calls_json array.
    // ArgumentsJson keeps the model's raw JSON arguments so rehydration rebuilds the
    // same dictionary the adapter serialized live.
    private sealed record StoredToolCall
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string ArgumentsJson { get; init; }
    }

    private static readonly JsonSerializerOptions ToolCallsSerializerOptions = new(JsonSerializerDefaults.Web);

    // Loads (or creates) the channel's conversation and builds the replay window for a
    // new turn. The window walks stored messages newest->oldest summing est_tokens until
    // WindowTokenBudget, backstopped by WindowMaxMessages — and never splits an assistant
    // tool-call message from its tool-result rows (a dangling tool_call_id is a provider 400).
    public async Task<ConversationTurnState> BeginTurnAsync(
        ulong channelDiscordId, ulong? guildDiscordId, CancellationToken cancellationToken)
    {
        var options = conversationOptions.Value;
        var conversation = await db.Conversations
            .SingleOrDefaultAsync(c => c.ChannelDiscordId == channelDiscordId, cancellationToken);
        if (conversation is null)
        {
            // The first two messages of a brand-new conversation can race this create;
            // GetOrInsertAsync resolves the unique-index conflict to the winner's row.
            var (existing, inserted) = await db.Conversations.GetOrInsertAsync(
                c => c.ChannelDiscordId == channelDiscordId,
                () => new ConversationEntity
                {
                    ChannelDiscordId = channelDiscordId,
                    GuildDiscordId = guildDiscordId,
                    CreatedAtUtc = DateTime.UtcNow,
                    LastActivityAtUtc = DateTime.UtcNow,
                },
                cancellationToken);
            conversation = existing ?? throw new InvalidOperationException(
                $"Conversation row for channel {channelDiscordId} vanished during get-or-insert");
            if (inserted)
                logger.LogDebug("Started conversation store for channel {ChannelId}", channelDiscordId);
        }

        var lastTurnIndex = await db.ConversationMessages
            .Where(m => m.ConversationId == conversation.Id)
            .MaxAsync(m => (int?)m.TurnIndex, cancellationToken);

        // The uuidv7 PK is insertion order; the newest WindowMaxMessages rows are the
        // most history the window may ever replay.
        var rows = await db.ConversationMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.Id)
            .Take(options.WindowMaxMessages)
            .ToListAsync(cancellationToken);
        rows.Reverse();

        var window = BuildWindow(rows, options.WindowTokenBudget);
        return new ConversationTurnState(conversation.Id, (lastTurnIndex ?? -1) + 1, window);
    }

    public async Task PersistUserMessageAsync(
        ConversationTurnState turn, string text, CancellationToken cancellationToken)
    {
        db.ConversationMessages.Add(new ConversationMessageEntity
        {
            ConversationId = turn.ConversationId,
            TurnIndex = turn.TurnIndex,
            Role = ConversationMessageRole.User,
            Text = text,
            EstTokens = EstimateTokens(text),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await TouchAndSaveAsync(turn.ConversationId, cancellationToken);
    }

    // Persists one round's assembled assistant messages (text + tool calls + reasoning).
    // The provider-reported round usage lands on the round's first assistant row.
    public async Task PersistAssistantMessagesAsync(
        ConversationTurnState turn, IEnumerable<ChatMessage> messages,
        int? promptTokens, int? completionTokens, CancellationToken cancellationToken)
    {
        var usageRecorded = false;
        foreach (var message in messages)
        {
            var text = message.Text;
            var reasoning = string.Concat(
                message.Contents.OfType<TextReasoningContent>().Select(content => content.Text));
            var toolCallsJson = SerializeToolCalls(message.Contents.OfType<FunctionCallContent>().ToList());
            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(reasoning) && toolCallsJson is null)
                continue;

            db.ConversationMessages.Add(new ConversationMessageEntity
            {
                ConversationId = turn.ConversationId,
                TurnIndex = turn.TurnIndex,
                Role = ConversationMessageRole.Assistant,
                Text = string.IsNullOrEmpty(text) ? null : text,
                ToolCallsJson = toolCallsJson,
                Reasoning = string.IsNullOrEmpty(reasoning) ? null : reasoning,
                PromptTokens = usageRecorded ? null : promptTokens,
                CompletionTokens = usageRecorded ? null : completionTokens,
                EstTokens = EstimateTokens(text, toolCallsJson, reasoning),
                CreatedAtUtc = DateTime.UtcNow,
            });
            usageRecorded = true;
        }

        await TouchAndSaveAsync(turn.ConversationId, cancellationToken);
    }

    public async Task PersistToolResultAsync(
        ConversationTurnState turn, string toolName, FunctionResultContent result,
        CancellationToken cancellationToken)
    {
        var wireText = ToWireText(result);
        db.ConversationMessages.Add(new ConversationMessageEntity
        {
            ConversationId = turn.ConversationId,
            TurnIndex = turn.TurnIndex,
            Role = ConversationMessageRole.Tool,
            ToolCallId = result.CallId,
            ToolName = toolName,
            ToolResult = wireText,
            EstTokens = EstimateTokens(wireText),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await TouchAndSaveAsync(turn.ConversationId, cancellationToken);
    }

    public async Task RecordUsageAsync(
        ConversationTurnState turn, ulong invokerId, ConversationRoundUsage usage,
        CancellationToken cancellationToken)
    {
        db.ConversationUsage.Add(new ConversationUsageEntity
        {
            ConversationId = turn.ConversationId,
            InvokerId = invokerId,
            TurnIndex = turn.TurnIndex,
            Round = usage.Round,
            Attempt = usage.Attempt,
            Model = usage.Model,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            CostUsd = usage.CostUsd,
            UpstreamInferenceCostUsd = usage.UpstreamInferenceCostUsd,
            WebSearchRequests = usage.WebSearchRequests,
            LatencyMs = usage.LatencyMs,
            Failed = usage.Failed,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    // The exact string MEAI's OpenAI adapter puts on the wire for a tool result: the
    // `Result as string` fast-path, else the same JSON serialization it falls back to.
    // Persisting THIS (and rehydrating Result as a string, which re-hits the fast-path)
    // makes the replayed tool message byte-identical whether the live Result was a raw
    // string or an AIFunctionFactory JsonElement (research A1/A-OQ1).
    private static string ToWireText(FunctionResultContent result)
    {
        if (result.Result is string text)
            return text;
        if (result.Result is null)
            return string.Empty;

        try
        {
            return JsonSerializer.Serialize(
                result.Result, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static string? SerializeToolCalls(IReadOnlyList<FunctionCallContent> calls)
    {
        if (calls.Count == 0)
            return null;

        var stored = calls.Select(call => new StoredToolCall
        {
            Id = call.CallId,
            Name = call.Name,
            ArgumentsJson = JsonSerializer.Serialize(
                call.Arguments ?? new Dictionary<string, object?>(), AIJsonUtilities.DefaultOptions),
        });
        return JsonSerializer.Serialize(stored, ToolCallsSerializerOptions);
    }

    // chars/4 — OpenRouter normalizes token counts to a GPT tokenizer, so this is a fair
    // zero-dependency proxy for budgeting; the ledger keeps the real provider numbers.
    private const int CharsPerEstimatedToken = 4;

    private static int EstimateTokens(params string?[] parts) =>
        Math.Max(1, parts.Sum(part => part?.Length ?? 0) / CharsPerEstimatedToken);

    // A window "group" is the unsplittable unit: an assistant message carrying tool calls
    // plus all its tool-result rows, or any other message alone. Incomplete groups (a
    // crash persisted the calls but not every result, or the backstop cut the head) are
    // dropped whole rather than replayed dangling.
    private IReadOnlyList<ChatMessage> BuildWindow(List<ConversationMessageEntity> rows, int tokenBudget)
    {
        var groups = GroupRows(rows);

        List<List<ConversationMessageEntity>> included = [];
        var totalTokens = 0;
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var groupTokens = groups[i].Sum(row => row.EstTokens);
            if (totalTokens + groupTokens > tokenBudget)
                break;
            totalTokens += groupTokens;
            included.Add(groups[i]);
        }
        included.Reverse();

        return included
            .SelectMany(group => group)
            .Select(Rehydrate)
            // A reasoning-only assistant row rehydrates to zero contents (reasoning is
            // stripped) — don't replay an empty message some providers could reject.
            .Where(message => message.Contents.Count > 0)
            .ToList();
    }

    private List<List<ConversationMessageEntity>> GroupRows(List<ConversationMessageEntity> rows)
    {
        List<List<ConversationMessageEntity>> groups = [];
        List<ConversationMessageEntity>? openGroup = null;
        HashSet<string>? pendingCallIds = null;

        foreach (var row in rows)
        {
            if (row.Role == ConversationMessageRole.Tool)
            {
                if (openGroup is not null && row.ToolCallId is not null && pendingCallIds!.Remove(row.ToolCallId))
                {
                    openGroup.Add(row);
                    continue;
                }

                // Orphan tool row: its assistant head fell outside the loaded rows (or
                // was never persisted) — replaying it dangling is a provider 400.
                logger.LogDebug("Dropping orphan tool row {RowId} from the replay window", row.Id);
                continue;
            }

            CloseGroup(groups, ref openGroup, ref pendingCallIds);

            if (row.Role == ConversationMessageRole.Assistant && row.ToolCallsJson is not null)
            {
                openGroup = [row];
                pendingCallIds = ParseToolCallIds(row.ToolCallsJson);
            }
            else
            {
                groups.Add([row]);
            }
        }

        CloseGroup(groups, ref openGroup, ref pendingCallIds);
        return groups;
    }

    // Seals the open tool-call group: kept only when every call got its result row.
    private void CloseGroup(
        List<List<ConversationMessageEntity>> groups,
        ref List<ConversationMessageEntity>? openGroup,
        ref HashSet<string>? pendingCallIds)
    {
        if (openGroup is null)
            return;

        if (pendingCallIds!.Count == 0)
            groups.Add(openGroup);
        else
            logger.LogDebug("Dropping incomplete tool-call group ({Missing} unanswered call(s)) from the replay window",
                pendingCallIds.Count);

        openGroup = null;
        pendingCallIds = null;
    }

    private HashSet<string> ParseToolCallIds(string toolCallsJson)
    {
        try
        {
            var calls = JsonSerializer.Deserialize<List<StoredToolCall>>(toolCallsJson, ToolCallsSerializerOptions) ?? [];
            return calls.Select(call => call.Id).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Unparseable tool_calls_json in the conversation store; treating the group as incomplete");
            return ["__unparseable__"];
        }
    }

    private ChatMessage Rehydrate(ConversationMessageEntity row)
    {
        switch (row.Role)
        {
            case ConversationMessageRole.User:
                return new ChatMessage(ChatRole.User, row.Text ?? string.Empty);

            case ConversationMessageRole.Tool:
                // Result MUST be a string so the adapter's fast-path emits the stored
                // wire text verbatim — see ToWireText.
                return new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(row.ToolCallId!, row.ToolResult ?? string.Empty)]);

            default:
                List<AIContent> contents = [];
                // Reasoning is persisted but deliberately NOT replayed — stale thinking
                // signatures (research A-OQ2); revisit if reply quality suffers.
                if (!string.IsNullOrEmpty(row.Text))
                    contents.Add(new TextContent(row.Text));
                if (row.ToolCallsJson is not null)
                    contents.AddRange(RehydrateToolCalls(row.ToolCallsJson));
                return new ChatMessage(ChatRole.Assistant, contents);
        }
    }

    private IEnumerable<FunctionCallContent> RehydrateToolCalls(string toolCallsJson)
    {
        var calls = JsonSerializer.Deserialize<List<StoredToolCall>>(toolCallsJson, ToolCallsSerializerOptions) ?? [];
        foreach (var call in calls)
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                call.ArgumentsJson, AIJsonUtilities.DefaultOptions) ?? [];
            yield return new FunctionCallContent(call.Id, call.Name, arguments);
        }
    }

    private async Task TouchAndSaveAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FindAsync([conversationId], cancellationToken);
        if (conversation is not null)
            conversation.LastActivityAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
