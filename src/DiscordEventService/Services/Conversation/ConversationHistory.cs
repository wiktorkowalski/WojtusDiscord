using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace DiscordEventService.Services.Conversation;

// Prepares one round's assembled response for the next request of the same turn (#426).
//
// The MEAI adapter reads server-tool blocks into WebSearchToolCallContent and
// WebSearchToolResultContent, but on the way back it sends only content whose
// RawRepresentation is a ContentBlockParam — it drops those two types. The API then misses
// the search: a turn paused mid-search (pause_turn) restarts it, and a server_tool_use left
// open next to a client tool call is never run. So each block is rebuilt as a request param,
// in place, so the order inside the assistant message (thinking, server_tool_use, result,
// text, tool_use) stays as the model wrote it.
internal static class ConversationHistory
{
    // A result block is kept even when its call is not in the same response: next to a client
    // tool call the API defers the search, and its result opens the NEXT response (#426). Only
    // the result of a call this method itself dropped goes too.
    public static List<ChatMessage> ForReplay(IEnumerable<ChatMessage> responseMessages, ILogger logger)
    {
        List<ChatMessage> replayable = [];
        var droppedCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in responseMessages)
        {
            List<AIContent> contents = [];
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case WebSearchToolCallContent call:
                        if (ToServerToolUse(call) is { } serverToolUse)
                        {
                            contents.Add(serverToolUse);
                        }
                        else
                        {
                            droppedCallIds.Add(call.CallId);
                            logger.LogDebug("Dropping web search call {CallId} with no query from the round history", call.CallId);
                        }
                        break;

                    case WebSearchToolResultContent { RawRepresentation: WebSearchToolResultBlock dropped }
                        when droppedCallIds.Contains(dropped.ToolUseID):
                        logger.LogDebug("Dropping the web search result of dropped call {CallId}", dropped.ToolUseID);
                        break;

                    case WebSearchToolResultContent { RawRepresentation: WebSearchToolResultBlock block }:
                        // The block's raw data as received: encrypted_content must go back
                        // byte for byte, or the API answers 400.
                        contents.Add(new AIContent
                        {
                            RawRepresentation = (ContentBlockParam)WebSearchToolResultBlockParam.FromRawUnchecked(block.RawData),
                        });
                        break;

                    // Before the case below: FunctionCallContent and FunctionResultContent
                    // derive from ToolCallContent and ToolResultContent.
                    case FunctionCallContent or FunctionResultContent or TextContent or TextReasoningContent:
                        contents.Add(content);
                        break;

                    case ToolCallContent or ToolResultContent:
                        // A server tool the loop does not declare (code execution needs dynamic
                        // filtering, which AnthropicChatOptions turns off): no way to send it back.
                        logger.LogDebug("Dropping unsupported server tool content {ContentType} from the round history",
                            content.GetType().Name);
                        break;

                    default:
                        contents.Add(content);
                        break;
                }
            }

            replayable.Add(new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
            });
        }

        return replayable;
    }

    // A refused response may hold tool calls the model wrote before the classifier stopped it.
    // Running them would act on a refused turn; the calls go, the rest stays.
    public static void StripToolCalls(List<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (!message.Contents.Any(content => content is FunctionCallContent))
                continue;

            messages[i] = new ChatMessage(message.Role,
                [.. message.Contents.Where(content => content is not FunctionCallContent)])
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
            };
        }
    }

    // The input of the stream's server_tool_use block is empty on content_block_start (the
    // query streams in input_json_delta), so its RawRepresentation cannot be sent back: the
    // block is rebuilt from the call id and the query the adapter assembled.
    private static AIContent? ToServerToolUse(WebSearchToolCallContent call)
    {
        if (SearchQuery(call) is not { } query)
            return null;

        return new AIContent
        {
            RawRepresentation = (ContentBlockParam)new ServerToolUseBlockParam
            {
                ID = call.CallId,
                Name = ServerToolUseBlockParamName.WebSearch,
                Input = new Dictionary<string, JsonElement> { ["query"] = JsonSerializer.SerializeToElement(query) },
            },
        };
    }

    // The query of a search call; a call without one is dropped from the history and from the render.
    public static string? SearchQuery(WebSearchToolCallContent call) => call.Queries is [var query, ..] ? query : null;

    // What the store keeps of one web search block (#440): the query of a server_tool_use, or
    // the sources (or error code) of a web_search_tool_result. A later turn gets this record
    // as text, not the blocks: their encrypted_content would fill the replay window.
    internal sealed record WebSearchRecord
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Query { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<WebSearchCitations.Citation>? Sources { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; init; }
    }

    // The sources a marker lists: enough to re-cite, short enough for the window.
    private const int MaxMarkerSources = 5;
    private const int MaxMarkerFieldLength = 120;

    // The web search blocks of one ForReplay message, in the model's order.
    public static List<WebSearchRecord> WebSearchRecords(ChatMessage message, ILogger logger)
    {
        List<WebSearchRecord> records = [];
        foreach (var content in message.Contents)
        {
            if (content.RawRepresentation is not ContentBlockParam block)
                continue;

            if (block.TryPickServerToolUse(out var call)
                && call.Input.TryGetValue("query", out var query) && query.ValueKind == JsonValueKind.String)
            {
                records.Add(new WebSearchRecord { Query = query.GetString() });
            }
            else if (block.TryPickWebSearchToolResult(out var result))
            {
                records.Add(ToRecord(result, logger));
            }
        }

        return records;
    }

    // The block came from FromRawUnchecked: an odd shape must not fail the turn's persist.
    private static WebSearchRecord ToRecord(WebSearchToolResultBlockParam result, ILogger logger)
    {
        try
        {
            if (result.Content.TryPickItem(out var results))
                return new WebSearchRecord { Sources = [.. results.Select(r => new WebSearchCitations.Citation(r.Url, r.Title))] };
            if (result.Content.TryPickRequestError(out var error))
                return new WebSearchRecord { Error = error.RawData.TryGetValue("error_code", out var code) ? code.ToString() : "unknown" };
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Unreadable web search result {CallId}; storing it without sources", result.ToolUseID);
        }

        return new WebSearchRecord { Error = "unreadable" };
    }

    // One line per record, e.g. `[web_search: "pogoda Kraków"]` or
    // `[web_search results: Pogoda — https://…; …]`. The system prompt tells the model what
    // these lines are and never to write one itself.
    public static string FormatMarkers(IEnumerable<WebSearchRecord> records) =>
        string.Join('\n', records.Select(FormatMarker));

    private static string FormatMarker(WebSearchRecord record)
    {
        if (record.Query is { } query)
            return $"[web_search: \"{Sanitize(query)}\"]";
        if (record.Sources?.Where(source => IsHttpUrl(source.Url)).Take(MaxMarkerSources).ToList() is { Count: > 0 } sources)
            return "[web_search results: " + string.Join("; ", sources.Select(FormatSource)) + "]";
        return $"[web_search failed: {Sanitize(record.Error ?? "no results")}]";
    }

    private static string FormatSource(WebSearchCitations.Citation source) =>
        Sanitize(source.Title) is { Length: > 0 } title ? $"{title} — {source.Url}" : source.Url;

    // Titles come from arbitrary web pages and replay as the assistant's own words: one line,
    // no brackets or quotes that could close the marker, and capped.
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var clean = new string([.. value.Select(c => char.IsControl(c) || c is '[' or ']' or '"' ? ' ' : c)]).Trim();
        return clean.Length <= MaxMarkerFieldLength ? clean : clean[..MaxMarkerFieldLength] + "…";
    }

    // Only http(s): the URL is replayed verbatim, so it must not carry a newline or another scheme.
    private static bool IsHttpUrl(string url) =>
        url.Length <= 2048
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '[' or ']');

    // Text of a response without the empty citation carriers: Claude splits an answer into
    // many text blocks (one per citation span), and ChatMessage.Text joins them all.
    public static string AnswerText(IEnumerable<ChatMessage> messages) =>
        string.Concat(messages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text));
}
