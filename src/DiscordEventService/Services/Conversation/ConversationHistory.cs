using System.Text.Json;
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
        if (call.Queries is not [var query, ..])
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

    // Text of a response without the empty citation carriers: Claude splits an answer into
    // many text blocks (one per citation span), and ChatMessage.Text joins them all.
    public static string AnswerText(IEnumerable<ChatMessage> messages) =>
        string.Concat(messages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text));
}
