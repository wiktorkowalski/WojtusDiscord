using Anthropic.Models.Messages;
using DiscordEventService.Configuration;
using Microsoft.Extensions.AI;

namespace DiscordEventService.Services.Conversation;

// Shapes every request of the loop for the Claude API (#426, ADR-0007): the native body
// fields, a stable tool order, and the prompt-cache breakpoints (#241, #271 before the move).
//
// The caller's message list and the shared AIFunctions are never mutated. The loop reuses
// its list for the next round, so a breakpoint left on an old message would add up past the
// API limit of four; every mark goes on a copy.
internal static class AnthropicChatOptions
{
    // The breakpoints a request may carry (API limit); see Shape for the three the loop uses.
    public const int MaxCacheBreakpoints = 4;

    // tools: the turn's function tools, in any order. allowToolCalls false is the post-cap
    // round: the same tools stay declared (the API rejects tool_use history with no tool
    // definitions, and thinking blocks are bound to the tool list they were made with), and
    // tool_choice "none" forces a text answer.
    public static ChatOptions Create(
        ConversationOptions options, IEnumerable<AITool> tools, bool allowToolCalls = true)
    {
        var effort = ParseEffort(options.ReasoningEffort);
        return new ChatOptions
        {
            // The factory bypasses ChatOptions.ModelId and MaxOutputTokens: Model and MaxTokens
            // must be set here. Messages and tools stay out — the adapter fills them in.
            RawRepresentationFactory = _ => new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = options.MaxOutputTokens,
                Messages = [],
                // Summarized: the stream carries thinking text the trace and the ledger keep.
                Thinking = new ThinkingConfigAdaptive { Display = Display.Summarized },
                OutputConfig = new OutputConfig { Effort = effort },
            },
            Tools = OrderTools(tools, options.WebSearch),
            ToolMode = allowToolCalls ? null : ChatToolMode.None,
        };
    }

    // Maps Conversation:ReasoningEffort to output_config.effort. Startup validation already
    // rejects anything else; this throws for a direct caller that skipped it.
    public static Effort ParseEffort(string reasoningEffort) => reasoningEffort switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(reasoningEffort), reasoningEffort,
            "Conversation:ReasoningEffort must be one of " + ConversationOptions.KnownReasoningEfforts),
    };

    // The render order of a request is tools, system, messages; a breakpoint caches everything
    // before it. Four breakpoints, at most:
    // - the last function tool (OrderTools): the tool schemas;
    // - the system prompt: tools + system, the same for every round and every turn in the TTL;
    // - the last message, only when tools are offered: the next round of the same turn reads
    //   this round's whole prefix. A round with no tools is the last one, so its 1.25× write
    //   would buy nothing;
    // - the previous request's last message (previousRequestCount), with the one above: the
    //   API looks back only ~20 blocks from a breakpoint for a hit, and one mixed round can
    //   add more. Its prefix is the one the previous round wrote, so this mark only reads.
    public static List<ChatMessage> ShapeMessages(
        IEnumerable<ChatMessage> messages, bool offersTools, int previousRequestCount = 0)
    {
        var shaped = messages.ToList();
        MarkLastContent(shaped, shaped.FindLastIndex(message => message.Role == ChatRole.System));
        if (!offersTools || shaped.Count == 0 || shaped[^1].Role == ChatRole.System)
            return shaped;

        MarkLastContent(shaped, shaped.Count - 1);
        if (previousRequestCount > 0 && previousRequestCount < shaped.Count
            && shaped[previousRequestCount - 1].Role != ChatRole.System)
            MarkLastContent(shaped, previousRequestCount - 1);
        return shaped;
    }

    // The tool list is part of the cached prefix: one changed byte or a different order is a
    // cache miss on everything after it. Function tools sorted by name, then the web search
    // server tool at a fixed place, last.
    private static List<AITool> OrderTools(IEnumerable<AITool> tools, WebSearchOptions webSearch)
    {
        var ordered = tools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count > 0 && ordered[^1] is AIFunction last)
            ordered[^1] = new CacheBreakpointFunction(last);

        if (webSearch.Enabled)
            ordered.Add(new ToolUnion(BuildWebSearchTool(webSearch)).AsAITool());

        return ordered;
    }

    // `web_search_20260209` with allowed_callers ["direct"]: the default lets the model run the
    // search from code execution (dynamic filtering), whose code-execution blocks the MEAI
    // adapter cannot send back on the next round. Direct calls give server_tool_use +
    // web_search_tool_result only, which ConversationHistory round-trips.
    // Optional fields are set only when configured: an init to null sends an explicit JSON null.
    internal static WebSearchTool20260209 BuildWebSearchTool(WebSearchOptions webSearch)
    {
        var tool = new WebSearchTool20260209
        {
            AllowedCallers = [WebSearchTool20260209AllowedCaller.Direct],
            MaxUses = webSearch.MaxUses,
        };
        if (webSearch.AllowedDomains is { Length: > 0 } allowed)
            tool = tool with { AllowedDomains = allowed };
        if (webSearch.BlockedDomains is { Length: > 0 } blocked)
            tool = tool with { BlockedDomains = blocked };
        if (BuildUserLocation(webSearch.UserLocation) is { } userLocation)
            tool = tool with { UserLocation = userLocation };
        return tool;
    }

    private static UserLocation? BuildUserLocation(WebSearchLocationOptions location)
    {
        if (string.IsNullOrWhiteSpace(location.City) && string.IsNullOrWhiteSpace(location.Region)
            && string.IsNullOrWhiteSpace(location.Country) && string.IsNullOrWhiteSpace(location.Timezone))
            return null;

        // Only the fields that are set: an init to null would send an explicit JSON null.
        var userLocation = new UserLocation();
        if (!string.IsNullOrWhiteSpace(location.City))
            userLocation = userLocation with { City = location.City };
        if (!string.IsNullOrWhiteSpace(location.Region))
            userLocation = userLocation with { Region = location.Region };
        if (!string.IsNullOrWhiteSpace(location.Country))
            userLocation = userLocation with { Country = location.Country };
        if (!string.IsNullOrWhiteSpace(location.Timezone))
            userLocation = userLocation with { Timezone = location.Timezone };
        return userLocation;
    }

    // Replaces the message with a copy whose last markable block carries the breakpoint. Text,
    // tool-result and the rebuilt web search blocks are markable; thinking is not, and the
    // adapter drops whitespace-only text with any mark on it. The walk goes back past those.
    private static void MarkLastContent(List<ChatMessage> messages, int index)
    {
        if (index < 0)
            return;

        var message = messages[index];
        for (var i = message.Contents.Count - 1; i >= 0; i--)
        {
            if (WithBreakpoint(message.Contents[i]) is not { } marked)
                continue;

            List<AIContent> contents = [.. message.Contents];
            contents[i] = marked;
            messages[index] = new ChatMessage(message.Role, contents) { AuthorName = message.AuthorName };
            return;
        }
    }

    // Default 5-minute TTL: the rounds of one turn start seconds apart. The adapter sends a
    // ContentBlockParam RawRepresentation as it is, so the mark goes on the param itself.
    private static AIContent? WithBreakpoint(AIContent content) => content switch
    {
        TextContent text when string.IsNullOrWhiteSpace(text.Text) => null,
        TextContent text =>
            new TextContent(text.Text) { Annotations = text.Annotations }.WithCacheControl(new CacheControlEphemeral()),
        FunctionResultContent result =>
            new FunctionResultContent(result.CallId, result.Result) { Exception = result.Exception }
                .WithCacheControl(new CacheControlEphemeral()),
        { RawRepresentation: ContentBlockParam block } when block.TryPickWebSearchToolResult(out var searchResult) =>
            new AIContent { RawRepresentation = (ContentBlockParam)(searchResult with { CacheControl = new CacheControlEphemeral() }) },
        { RawRepresentation: ContentBlockParam block } when block.TryPickServerToolUse(out var serverToolUse) =>
            new AIContent { RawRepresentation = (ContentBlockParam)(serverToolUse with { CacheControl = new CacheControlEphemeral() }) },
        _ => null,
    };

    // A copy of a shared AIFunction with the tool breakpoint added; the adapter reads
    // AdditionalProperties["CacheControl"] into the tool definition.
    private sealed class CacheBreakpointFunction : DelegatingAIFunction
    {
        public CacheBreakpointFunction(AIFunction innerFunction)
            : base(innerFunction)
        {
            AdditionalProperties = new Dictionary<string, object?>(innerFunction.AdditionalProperties)
            {
                [nameof(Tool.CacheControl)] = new CacheControlEphemeral(),
            };
        }

        public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; }
    }
}
