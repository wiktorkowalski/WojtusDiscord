using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiscordEventService.Tests;

// The request shape of the Claude API (#426), asserted on the bytes the real AnthropicClient +
// MEAI adapter send: the native body fields from RawRepresentationFactory, the tool order, the
// web search server tool, and the prompt-cache breakpoints (≤ 4, on copies only).
public sealed class AnthropicChatOptionsTests
{
    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public async Task Create_SendsModelMaxTokensAdaptiveThinkingAndEffort(string effort)
    {
        var options = new ConversationOptions { ReasoningEffort = effort, MaxOutputTokens = 16000 };

        var request = await SendOnceAsync(options, Tools("b", "a"));

        Assert.Equal("claude-sonnet-5-5", request["model"]!.GetValue<string>());
        Assert.Equal(16000, request["max_tokens"]!.GetValue<int>());
        Assert.Equal("adaptive", request["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("summarized", request["thinking"]!["display"]!.GetValue<string>());
        Assert.Equal(effort, request["output_config"]!["effort"]!.GetValue<string>());
        // No sampling knobs: thinking models reject a changed temperature.
        Assert.False(request.ContainsKey("temperature"));
        Assert.False(request.ContainsKey("top_p"));
        Assert.True(request["stream"]!.GetValue<bool>());
    }

    [Fact]
    public void ParseEffort_UnknownValue_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AnthropicChatOptions.ParseEffort("minimal"));

    [Fact]
    public async Task Tools_SortedByName_ThenWebSearchLast_WithItsOptions()
    {
        var options = new ConversationOptions
        {
            WebSearch = new WebSearchOptions
            {
                MaxUses = 4,
                BlockedDomains = ["spam.example"],
                UserLocation = new WebSearchLocationOptions { City = "Kraków", Country = "PL", Timezone = "Europe/Warsaw" },
            },
        };

        var request = await SendOnceAsync(options, Tools("zeta", "alpha", "mid"));

        var tools = request["tools"]!.AsArray();
        Assert.Equal(["alpha", "mid", "zeta", "web_search"], tools.Select(t => t!["name"]!.GetValue<string>()));
        var search = tools[^1]!;
        Assert.Equal("web_search_20260209", search["type"]!.GetValue<string>());
        Assert.Equal(4, search["max_uses"]!.GetValue<int>());
        // Direct calls only: dynamic filtering's code-execution blocks cannot round-trip.
        Assert.Equal(["direct"], search["allowed_callers"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal(["spam.example"], search["blocked_domains"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.False(search.AsObject().ContainsKey("allowed_domains"));
        Assert.Equal("approximate", search["user_location"]!["type"]!.GetValue<string>());
        Assert.Equal("Kraków", search["user_location"]!["city"]!.GetValue<string>());
        Assert.Equal("PL", search["user_location"]!["country"]!.GetValue<string>());
        Assert.False(search["user_location"]!.AsObject().ContainsKey("region"));
    }

    [Fact]
    public async Task WebSearchDisabled_NoServerTool()
    {
        var options = new ConversationOptions { WebSearch = new WebSearchOptions { Enabled = false } };

        var request = await SendOnceAsync(options, Tools("a"));

        Assert.DoesNotContain(request["tools"]!.AsArray(), t => t!["type"]?.GetValue<string>() == "web_search_20260209");
    }

    [Fact]
    public async Task StrictFlag_ReachesTheToolDefinition()
    {
        var strict = AIFunctionFactory.Create(() => "x", new AIFunctionFactoryOptions
        {
            Name = "strict_one",
            AdditionalProperties = new Dictionary<string, object?> { ["Strict"] = true },
        });

        var request = await SendOnceAsync(new ConversationOptions(), [strict]);

        var tool = request["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "strict_one")!;
        Assert.True(tool["strict"]!.GetValue<bool>());
    }

    // Three breakpoints in a tool round: last function tool, system prompt, last message.
    // A whole tool loop reuses one message list, so the marks must go on copies: the second
    // round (with the first round's messages still in the list) must not carry more.
    [Fact]
    public async Task CacheBreakpoints_LastToolSystemAndLastMessage_NeverAccumulate()
    {
        var options = new ConversationOptions();
        var chatOptions = AnthropicChatOptions.Create(options, Tools("b", "a"));
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "system prompt"),
            new(ChatRole.User, "first"),
        ];
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("ok"));
        var client = handler.CreateChatClient(options);

        await DrainAsync(client, AnthropicChatOptions.ShapeMessages(messages, offersTools: true), chatOptions);
        messages.Add(new ChatMessage(ChatRole.Assistant, "answer"));
        messages.Add(new ChatMessage(ChatRole.User, "second"));
        await DrainAsync(client, AnthropicChatOptions.ShapeMessages(messages, offersTools: true), chatOptions);

        // The caller's messages were never marked.
        Assert.All(messages, m => Assert.All(m.Contents, c => Assert.True(c.AdditionalProperties is null or { Count: 0 })));

        foreach (var request in handler.Requests)
        {
            Assert.Equal(3, ClaudeRequest.CountCacheBreakpoints(request));
            Assert.InRange(ClaudeRequest.CountCacheBreakpoints(request), 0, AnthropicChatOptions.MaxCacheBreakpoints);
            Assert.NotNull(request["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "b")!["cache_control"]);
            Assert.NotNull(request["system"]!.AsArray()[^1]!["cache_control"]);
            var last = ClaudeRequest.Messages(request)[^1]!["content"]!.AsArray()[^1]!;
            Assert.NotNull(last["cache_control"]);
        }
        Assert.Equal("second", ClaudeRequest.Messages(handler.Requests[1])[^1]!["content"]![0]!["text"]!.GetValue<string>());
    }

    // The post-cap round: the same tools stay declared (tool_use history needs them), calls are
    // forbidden, and no last-message breakpoint is written for a round that has no successor.
    [Fact]
    public async Task NoToolCalls_KeepsToolsDeclared_SendsToolChoiceNone()
    {
        var options = new ConversationOptions();
        var chatOptions = AnthropicChatOptions.Create(options, Tools("a"), allowToolCalls: false);
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("ok"));
        List<ChatMessage> messages = [new(ChatRole.System, "system"), new(ChatRole.User, "q")];

        await DrainAsync(handler.CreateChatClient(options),
            AnthropicChatOptions.ShapeMessages(messages, offersTools: false), chatOptions);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("none", request["tool_choice"]!["type"]!.GetValue<string>());
        Assert.Contains(request["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "a");
        Assert.Null(ClaudeRequest.Messages(request)[^1]!["content"]!.AsArray()[^1]!["cache_control"]);
        Assert.Equal(2, ClaudeRequest.CountCacheBreakpoints(request));
    }

    // #452: a paused round ends in a rebuilt search block. The adapter sends its param as it
    // is, so the breakpoint goes on the param itself; text-only marking left the round uncached.
    [Fact]
    public async Task PausedSearchTail_CarriesTheLastMessageBreakpoint()
    {
        var options = new ConversationOptions();
        var chatOptions = AnthropicChatOptions.Create(options, Tools("a"));
        var handler = ScriptedClaudeHandler.Streams(call => call switch
        {
            0 => ClaudeSse.Start()
                .WebSearch("srvtoolu_1", "pogoda", ("https://weather.example/krk", "Weather"))
                .End("pause_turn", webSearches: 1),
            _ => ClaudeSse.TextAnswer("ok"),
        });
        var client = handler.CreateChatClient(options);
        List<ChatMessage> messages = [new(ChatRole.System, "system"), new(ChatRole.User, "q")];

        var updates = await DrainAsync(client, AnthropicChatOptions.ShapeMessages(messages, offersTools: true), chatOptions);
        messages.AddRange(ConversationHistory.ForReplay(updates.ToChatResponse().Messages, NullLogger.Instance));
        await DrainAsync(client, AnthropicChatOptions.ShapeMessages(messages, offersTools: true), chatOptions);

        var request = handler.Requests[1];
        Assert.Equal(3, ClaudeRequest.CountCacheBreakpoints(request));
        var last = ClaudeRequest.Messages(request)[^1]!;
        Assert.Equal(["server_tool_use", "web_search_tool_result"], ClaudeRequest.BlockTypes(last));
        Assert.NotNull(last["content"]![1]!["cache_control"]);
        // encrypted_content still goes back byte for byte.
        Assert.Equal("ENC_Weather", last["content"]![1]!["content"]![0]!["encrypted_content"]!.GetValue<string>());
    }

    // #452: a whitespace-only last text is dropped by the adapter; the mark goes on the block before it.
    [Fact]
    public async Task WhitespaceTail_MarksTheBlockBeforeIt()
    {
        var options = new ConversationOptions();
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("ok"));
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "system"),
            new(ChatRole.User, "q"),
            new(ChatRole.Assistant, [new TextContent("szukam"), new TextContent(" ")]),
        ];

        await DrainAsync(handler.CreateChatClient(options),
            AnthropicChatOptions.ShapeMessages(messages, offersTools: true), AnthropicChatOptions.Create(options, Tools("a")));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(3, ClaudeRequest.CountCacheBreakpoints(request));
        Assert.NotNull(ClaudeRequest.Messages(request)[^1]!["content"]![0]!["cache_control"]);
    }

    // #452: round 2+ also marks the previous request's tail, so a long round cannot push the
    // cache hit out of the API's lookback window. Four marks: the API limit.
    [Fact]
    public async Task PreviousRequestTail_GetsTheFourthBreakpoint()
    {
        var options = new ConversationOptions();
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("ok"));
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "system"),
            new(ChatRole.User, "q"),
            new(ChatRole.Assistant, "narration"),
            new(ChatRole.User, "more"),
        ];

        await DrainAsync(handler.CreateChatClient(options),
            AnthropicChatOptions.ShapeMessages(messages, offersTools: true, previousRequestCount: 2),
            AnthropicChatOptions.Create(options, Tools("a")));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(AnthropicChatOptions.MaxCacheBreakpoints, ClaudeRequest.CountCacheBreakpoints(request));
        Assert.NotNull(ClaudeRequest.Messages(request)[0]!["content"]!.AsArray()[^1]!["cache_control"]);
    }

    private static List<AITool> Tools(params string[] names) =>
        [.. names.Select(name => AIFunctionFactory.Create(() => name, new AIFunctionFactoryOptions { Name = name }))];

    private static async Task<JsonObject> SendOnceAsync(ConversationOptions options, IEnumerable<AITool> tools)
    {
        var handler = ScriptedClaudeHandler.Streams(_ => ClaudeSse.TextAnswer("ok"));
        List<ChatMessage> messages = [new(ChatRole.System, "system"), new(ChatRole.User, "q")];
        await DrainAsync(handler.CreateChatClient(options),
            AnthropicChatOptions.ShapeMessages(messages, offersTools: true),
            AnthropicChatOptions.Create(options, tools));
        return Assert.Single(handler.Requests);
    }

    private static async Task<List<ChatResponseUpdate>> DrainAsync(
        IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options)
    {
        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.GetStreamingResponseAsync(messages, options))
            updates.Add(update);
        return updates;
    }
}
