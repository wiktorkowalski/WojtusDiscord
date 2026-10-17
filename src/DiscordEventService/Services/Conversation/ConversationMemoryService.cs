using System.Text.Json;
using DiscordEventService.Configuration;
using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Conversations;
using DiscordEventService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// A turn's handle into the conversation store (#267): which conversation it belongs to,
// its monotonic turn index, and the token-windowed replay of everything stored so far.
internal sealed record ConversationTurnState(
    Guid ConversationId, int TurnIndex, IReadOnlyList<ChatMessage> Window);

// One model-call attempt's ledger row content (#267, #426). §1 always writes Attempt=1 and
// Failed=false; the §2 retry policy adds attempt>1 and failed rows (which may still bill).
// PromptTokens is the whole input (uncached + cache read + cache write); CostUsd is computed
// from the price table and includes the search fee; UpstreamInferenceCostUsd is the same
// cost without the search fee.
internal sealed record ConversationRoundUsage(
    int Round, int Attempt, string Model,
    int? PromptTokens, int? CompletionTokens, int? CacheReadTokens, int? CacheWriteTokens,
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
            .Where(m => m.ConversationId == conversation.Id && !m.Refused)
            .OrderByDescending(m => m.Id)
            .Take(options.WindowMaxMessages)
            .ToListAsync(cancellationToken);
        rows.Reverse();

        var window = BuildWindow(rows, options.WindowTokenBudget);
        return new ConversationTurnState(conversation.Id, (lastTurnIndex ?? -1) + 1, window);
    }

    // Returns the message as the model sees it, built by the same Rehydrate a later turn
    // replays it with: the current turn and its replay are byte-identical (#461).
    public async Task<ChatMessage> PersistUserMessageAsync(
        ConversationTurnState turn, string text, ulong authorId, string authorName,
        CancellationToken cancellationToken)
    {
        var row = new ConversationMessageEntity
        {
            ConversationId = turn.ConversationId,
            TurnIndex = turn.TurnIndex,
            Role = ConversationMessageRole.User,
            Text = text,
            AuthorId = authorId,
            AuthorName = SanitizeAuthorName(authorName),
            CreatedAtUtc = DateTime.UtcNow,
        };
        row.EstTokens = EstimateTokens(UserText(row));
        db.ConversationMessages.Add(row);
        await TouchAndSaveAsync(turn.ConversationId, cancellationToken);
        return Rehydrate(row, webSearchMarkers: null);
    }

    // Takes the whole turn out of every later window (#450): the user row, the partial answer
    // and any tool rows. The store stays append-only; the rows are flagged, not deleted.
    public async Task MarkTurnRefusedAsync(ConversationTurnState turn, CancellationToken cancellationToken) =>
        await db.ConversationMessages
            .Where(m => m.ConversationId == turn.ConversationId && m.TurnIndex == turn.TurnIndex)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Refused, true), cancellationToken);

    // A user row's text under a bot-added author line (#461), e.g. `[from: Damrad, id 123]`.
    // Several people can write in one thread; without the line the model takes them for one
    // person. The system prompt says the bot writes this line. Rows from before #461 carry no
    // author and replay as plain text.
    private static string UserText(ConversationMessageEntity row) =>
        row.AuthorId is { } authorId
            ? $"[from: {row.AuthorName}, id {authorId}]\n{row.Text}"
            : row.Text ?? string.Empty;

    private const int AuthorNameMaxLength = 64;

    private static string SanitizeAuthorName(string name) =>
        ConversationHistory.Sanitize(name, AuthorNameMaxLength) is { Length: > 0 } clean ? clean : "?";

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
            var webSearches = ConversationHistory.WebSearchRecords(message, logger);
            var webSearchesJson = webSearches.Count == 0 ? null : JsonSerializer.Serialize(webSearches, ToolCallsSerializerOptions);
            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(reasoning) && toolCallsJson is null
                && webSearchesJson is null)
                continue;

            db.ConversationMessages.Add(new ConversationMessageEntity
            {
                ConversationId = turn.ConversationId,
                TurnIndex = turn.TurnIndex,
                Role = ConversationMessageRole.Assistant,
                Text = string.IsNullOrEmpty(text) ? null : text,
                ToolCallsJson = toolCallsJson,
                WebSearchesJson = webSearchesJson,
                Reasoning = string.IsNullOrEmpty(reasoning) ? null : reasoning,
                PromptTokens = usageRecorded ? null : promptTokens,
                CompletionTokens = usageRecorded ? null : completionTokens,
                // The markers, not the stored JSON: the window carries what replay sends.
                EstTokens = EstimateTokens(text, toolCallsJson, ConversationHistory.FormatMarkers(webSearches), reasoning),
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
            CacheReadTokens = usage.CacheReadTokens,
            CacheWriteTokens = usage.CacheWriteTokens,
            CostUsd = usage.CostUsd,
            UpstreamInferenceCostUsd = usage.UpstreamInferenceCostUsd,
            WebSearchRequests = usage.WebSearchRequests,
            LatencyMs = usage.LatencyMs,
            Failed = usage.Failed,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        // After the save, so the metric and the ledger count the same rows.
        BotMetrics.ConversationRoundRecorded(
            usage.Model, usage.Attempt, usage.PromptTokens, usage.CompletionTokens,
            usage.CostUsd, usage.LatencyMs, usage.Failed);

        // The fee of the searches is what the call cost above its tokens. Langfuse does not
        // show it; this counter and the ledger row do.
        if (usage.WebSearchRequests is > 0 and var searches)
            BotMetrics.ConversationWebSearched(usage.Model, searches, usage.CostUsd - usage.UpstreamInferenceCostUsd);
    }

    // The exact string the MEAI Anthropic adapter puts in a tool_result block: a string Result
    // goes as is, anything else (an AIFunctionFactory JsonElement) as its JSON serialization
    // with AIJsonUtilities.DefaultOptions — the same rule the OpenAI adapter had (#426).
    // Persisting THIS (and rehydrating Result as a string, which re-hits the string path)
    // makes the replayed tool_result byte-identical whether the live Result was a raw
    // string or a JsonElement (research A1/A-OQ1).
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

    // chars/4 — a zero-dependency proxy for budgeting; the ledger keeps the real token
    // numbers. The Claude tokenizer gives more tokens per character than this, so the window
    // budget is soft; compare against the ledger's prompt_tokens before changing it (#426).
    private const int CharsPerEstimatedToken = 4;

    private static int EstimateTokens(params string?[] parts) =>
        Math.Max(1, parts.Sum(part => part?.Length ?? 0) / CharsPerEstimatedToken);

    // A window "group" is the unsplittable unit: an assistant message carrying tool calls
    // plus all its tool-result rows, or any other message alone. Incomplete groups (a
    // crash persisted the calls but not every result, or the backstop cut the head) are
    // dropped whole rather than replayed dangling.
    private IReadOnlyList<ChatMessage> BuildWindow(List<ConversationMessageEntity> rows, int tokenBudget)
    {
        var markers = WebSearchMarkers(rows);
        var groups = GroupRows(rows, markers);

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
        DropUnreplayableEdges(included);

        return included.SelectMany(group => RehydrateGroup(group, markers)).ToList();
    }

    // Each row's web search markers, keyed by row id; a row without searches has no entry.
    // Rendered per turn over all loaded rows, before the grouping: a search's call and result
    // pair across rows (#440), and a row whose only search moved to its result's row renders
    // nothing.
    private static Dictionary<Guid, string> WebSearchMarkers(List<ConversationMessageEntity> rows)
    {
        Dictionary<Guid, string> markers = [];
        foreach (var turn in rows.Where(row => row.WebSearchesJson is not null).GroupBy(row => row.TurnIndex))
        {
            var turnRows = turn.ToList();
            var rendered = ConversationHistory.FormatTurnMarkers([.. turnRows.Select(row => ParseWebSearches(row.WebSearchesJson!))]);
            foreach (var (row, text) in turnRows.Zip(rendered))
                markers[row.Id] = text;
        }

        return markers;
    }

    private static List<ConversationHistory.WebSearchRecord> ParseWebSearches(string webSearchesJson) =>
        JsonSerializer.Deserialize<List<ConversationHistory.WebSearchRecord>>(webSearchesJson, ToolCallsSerializerOptions) ?? [];

    // Two shapes the Claude API rejects with thinking on (#426), trimmed from the window:
    // - a window that starts with an assistant row (the budget cut off its user row): the
    //   first message must be a user message;
    // - a last assistant message that is a tool-call group: a turn that failed after its tool
    //   results left no answer row. The API reads the next request as a continuation of that
    //   tool loop, which needs the round's thinking block — replay strips it, so a 400 on
    //   every later turn until the group leaves the window.
    private void DropUnreplayableEdges(List<List<ConversationMessageEntity>> groups)
    {
        while (groups.Count > 0 && groups[0][0].Role != ConversationMessageRole.User)
            groups.RemoveAt(0);

        while (groups.FindLastIndex(group => group[0].Role == ConversationMessageRole.Assistant) is var last
               && last >= 0 && groups[last][0].ToolCallsJson is not null)
        {
            logger.LogDebug("Dropping an unanswered tool-call group from the end of the replay window");
            groups.RemoveAt(last);
        }
    }

    // The tool-result rows of a group replay as ONE Tool message, as the live loop sent them:
    // the Claude API wants every tool_result of a tool_use message in the single user message
    // right after it, and the adapter turns each ChatMessage into its own message (#426).
    private IEnumerable<ChatMessage> RehydrateGroup(
        List<ConversationMessageEntity> group, Dictionary<Guid, string> markers)
    {
        var results = group.Where(row => row.Role == ConversationMessageRole.Tool).ToList();
        foreach (var row in group.Where(row => row.Role != ConversationMessageRole.Tool))
            yield return Rehydrate(row, markers.GetValueOrDefault(row.Id));

        if (results.Count > 0)
            yield return new ChatMessage(ChatRole.Tool, [.. results.Select(RehydrateToolResult)]);
    }

    // Result MUST be a string so the adapter emits the stored wire text verbatim — see ToWireText.
    private static AIContent RehydrateToolResult(ConversationMessageEntity row) =>
        new FunctionResultContent(row.ToolCallId!, row.ToolResult ?? string.Empty);

    private List<List<ConversationMessageEntity>> GroupRows(
        List<ConversationMessageEntity> rows, Dictionary<Guid, string> markers)
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

            // A reasoning-only assistant row (a round that ended with thinking alone) replays as
            // nothing: reasoning is stripped. So does a row whose only search renders on its
            // result's row. Skipped HERE, before the edge trimming, so the
            // edges are checked on the rows the request carries — counted as the answer, it
            // would hide an unanswered tool group in front of it (#426).
            if (row.Role == ConversationMessageRole.Assistant
                && string.IsNullOrEmpty(row.Text) && row.ToolCallsJson is null
                && string.IsNullOrEmpty(markers.GetValueOrDefault(row.Id)))
                continue;

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

    private ChatMessage Rehydrate(ConversationMessageEntity row, string? webSearchMarkers)
    {
        switch (row.Role)
        {
            case ConversationMessageRole.User:
                return new ChatMessage(ChatRole.User, UserText(row));

            case ConversationMessageRole.Tool:
                return new ChatMessage(ChatRole.Tool, [RehydrateToolResult(row)]);

            default:
                List<AIContent> contents = [];
                // Reasoning is persisted but deliberately NOT replayed — stale thinking
                // signatures (research A-OQ2); revisit if reply quality suffers.
                // The searches as text markers before the row's text (#440): the model sees the
                // search behind an answer and does not retract it as invented.
                if (!string.IsNullOrEmpty(webSearchMarkers))
                    contents.Add(new TextContent(webSearchMarkers));
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
