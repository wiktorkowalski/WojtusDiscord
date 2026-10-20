using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordEventService.Tests;

// One streamed Messages API response in the SSE wire format of
// https://api.anthropic.com/v1/messages (stream: true), built block by block (#426).
// Every block gets the next content index; End() closes the message.
internal sealed class ClaudeSse
{
    public const string Model = "claude-sonnet-5-5";
    public const string Signature = "SIG_ABC";

    private readonly StringBuilder _sse = new();
    private int _index;

    // message_start with the input usage: input_tokens is the UNCACHED part, as on the wire.
    public static ClaudeSse Start(int input = 100, int cacheRead = 0, int cacheWrite = 0)
    {
        var sse = new ClaudeSse();
        sse.Event("message_start", new JsonObject
        {
            ["type"] = "message_start",
            ["message"] = new JsonObject
            {
                ["id"] = "msg_test",
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = Model,
                ["content"] = new JsonArray(),
                ["stop_reason"] = null,
                ["stop_sequence"] = null,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input,
                    ["output_tokens"] = 1,
                    ["cache_creation_input_tokens"] = cacheWrite,
                    ["cache_read_input_tokens"] = cacheRead,
                },
            },
        });
        return sse;
    }

    // A thinking block: its text, then the signature, as two deltas.
    public ClaudeSse Thinking(string text = "private reasoning", string signature = Signature)
    {
        var index = BlockStart(new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "" });
        Delta(index, new JsonObject { ["type"] = "thinking_delta", ["thinking"] = text });
        Delta(index, new JsonObject { ["type"] = "signature_delta", ["signature"] = signature });
        return BlockStop(index);
    }

    // A text block, optionally followed by web-search citations (one citations_delta each).
    public ClaudeSse Text(string text, params (string Url, string Title)[] citations) =>
        TextCiting(text, [.. citations.Select(c => (c.Url, c.Title, "cited"))]);

    // A text block whose citations carry their own cited_text.
    public ClaudeSse TextCiting(string text, params (string Url, string Title, string CitedText)[] citations)
    {
        var index = BlockStart(new JsonObject { ["type"] = "text", ["text"] = "" });
        Delta(index, new JsonObject { ["type"] = "text_delta", ["text"] = text });
        foreach (var (url, title, citedText) in citations)
        {
            Delta(index, new JsonObject
            {
                ["type"] = "citations_delta",
                ["citation"] = new JsonObject
                {
                    ["type"] = "web_search_result_location",
                    ["url"] = url,
                    ["title"] = title,
                    ["encrypted_index"] = "EIDX",
                    ["cited_text"] = citedText,
                },
            });
        }
        return BlockStop(index);
    }

    // A client tool call; the input streams as one input_json_delta.
    public ClaudeSse ToolUse(string id, string name, string inputJson)
    {
        var index = BlockStart(new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = id,
            ["name"] = name,
            ["input"] = new JsonObject(),
            ["caller"] = new JsonObject { ["type"] = "direct" },
        });
        Delta(index, new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = inputJson });
        return BlockStop(index);
    }

    // A server-side web search: the server_tool_use block (query streamed) and its result block.
    public ClaudeSse WebSearch(string id, string query, params (string Url, string Title)[] results) =>
        WebSearchCall(id, query).WebSearchResult(id, results);

    // The server_tool_use block alone: next to a client tool call the API does not run the
    // search yet; its result opens the NEXT response (WebSearchResult).
    public ClaudeSse WebSearchCall(string id, string query)
    {
        var call = BlockStart(new JsonObject
        {
            ["type"] = "server_tool_use",
            ["id"] = id,
            ["name"] = "web_search",
            ["input"] = new JsonObject(),
            ["caller"] = new JsonObject { ["type"] = "direct" },
        });
        Delta(call, new JsonObject
        {
            ["type"] = "input_json_delta",
            ["partial_json"] = new JsonObject { ["query"] = query }.ToJsonString(),
        });
        return BlockStop(call);
    }

    public ClaudeSse WebSearchResult(string id, params (string Url, string Title)[] results)
    {
        var content = new JsonArray();
        foreach (var (url, title) in results)
        {
            content.Add(new JsonObject
            {
                ["type"] = "web_search_result",
                ["url"] = url,
                ["title"] = title,
                ["encrypted_content"] = "ENC_" + title,
                ["page_age"] = null,
            });
        }

        var result = BlockStart(new JsonObject
        {
            ["type"] = "web_search_tool_result",
            ["tool_use_id"] = id,
            ["content"] = content,
            ["caller"] = new JsonObject { ["type"] = "direct" },
        });
        return BlockStop(result);
    }

    // message_delta (stop reason + cumulative output usage) and message_stop.
    public string End(
        string stopReason = "end_turn", int output = 20, int webSearches = 0, string? refusalCategory = null)
    {
        var usage = new JsonObject { ["output_tokens"] = output };
        if (webSearches > 0)
            usage["server_tool_use"] = new JsonObject { ["web_search_requests"] = webSearches, ["web_fetch_requests"] = 0 };

        Event("message_delta", new JsonObject
        {
            ["type"] = "message_delta",
            ["delta"] = new JsonObject
            {
                ["stop_reason"] = stopReason,
                ["stop_sequence"] = null,
                ["stop_details"] = refusalCategory is null
                    ? null
                    : new JsonObject
                    {
                        ["type"] = "refusal",
                        ["category"] = refusalCategory,
                        ["explanation"] = "declined",
                    },
            },
            ["usage"] = usage,
        });
        Event("message_stop", new JsonObject { ["type"] = "message_stop" });
        return _sse.ToString();
    }

    // The stream dies mid-way: an `error` event, no message_delta / message_stop after it.
    public string Error(string type = "overloaded_error")
    {
        Event("error", new JsonObject
        {
            ["type"] = "error",
            ["error"] = new JsonObject { ["type"] = type, ["message"] = "scripted " + type },
        });
        return _sse.ToString();
    }

    // The events so far, with no end: a stream cut off here.
    public override string ToString() => _sse.ToString();

    // The usual one-block answer.
    public static string TextAnswer(string text, int input = 100, int output = 20) =>
        Start(input).Text(text).End(output: output);

    private int BlockStart(JsonObject block)
    {
        var index = _index++;
        Event("content_block_start", new JsonObject
        {
            ["type"] = "content_block_start",
            ["index"] = index,
            ["content_block"] = block,
        });
        return index;
    }

    private void Delta(int index, JsonObject delta) =>
        Event("content_block_delta", new JsonObject
        {
            ["type"] = "content_block_delta",
            ["index"] = index,
            ["delta"] = delta,
        });

    private ClaudeSse BlockStop(int index)
    {
        Event("content_block_stop", new JsonObject { ["type"] = "content_block_stop", ["index"] = index });
        return this;
    }

    private void Event(string name, JsonObject data) =>
        _sse.Append("event: ").Append(name).Append('\n')
            .Append("data: ").Append(data.ToJsonString()).Append("\n\n");
}

// Answers every request from a script and keeps what was sent. The seam sits UNDER the real
// AnthropicClient + MEAI adapter, so the asserted bytes are the ones production sends.
internal sealed class ScriptedClaudeHandler : HttpMessageHandler
{
    private readonly Func<int, CancellationToken, Task<HttpResponseMessage>> _responder;

    public ScriptedClaudeHandler(Func<int, HttpResponseMessage> responder)
        : this((call, _) => Task.FromResult(responder(call)))
    {
    }

    // For a response that waits (a hung server): the token is the SDK's attempt token.
    public ScriptedClaudeHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> responder) =>
        _responder = responder;

    // Never answers: the request ends only when its token is cancelled.
    public static async Task<HttpResponseMessage> Hang(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("unreachable");
    }

    // Every call streams the scripted SSE body of its index.
    public static ScriptedClaudeHandler Streams(Func<int, string> sse) => new(call => Sse(sse(call)));

    public List<string> RequestBodies { get; } = [];

    public List<JsonObject> Requests => RequestBodies.Select(body => JsonNode.Parse(body)!.AsObject()).ToList();

    public int CallCount => RequestBodies.Count;

    public static HttpResponseMessage Sse(string sse) =>
        new(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };

    // The body streams the scripted events, then hangs until the token is cancelled: a
    // stream that billed its input (message_start) and then stalled.
    public static HttpResponseMessage SseThenHang(string sse, CancellationToken hangUntil) =>
        new(HttpStatusCode.OK) { Content = new HangingContent(Encoding.UTF8.GetBytes(sse), hangUntil) };

    public static HttpResponseMessage Status(int status, int? retryAfterMs = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(
                $$"""{"type":"error","error":{"type":"api_error","message":"scripted {{status}}"},"request_id":"req_test"}""",
                Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("request-id", "req_test");
        if (retryAfterMs is not null)
            response.Headers.TryAddWithoutValidation("retry-after-ms", retryAfterMs.Value.ToString(CultureInfo.InvariantCulture));
        return response;
    }

    // The production chat client (ConversationRegistration) over this transport. An
    // attemptTimeout replaces the SDK's 60 s AttemptTimeout, so a test can run one out.
    public IChatClient CreateChatClient(
        ConversationOptions options, ILogger? logger = null, TimeSpan? attemptTimeout = null)
    {
        var client = ConversationRegistration.CreateApiClient(
            "test-key", logger ?? NullLogger.Instance,
            new HttpClient(this) { Timeout = Timeout.InfiniteTimeSpan });
        if (attemptTimeout is { } timeout)
            client = client.WithOptions(clientOptions => clientOptions with { Timeout = timeout });

        return ConversationRegistration.BuildChatClient(client, options, enableSensitiveData: false);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = RequestBodies.Count;
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return await _responder(index, cancellationToken);
    }
}

internal static class ClaudeRequest
{
    // Every JSON object in a request that carries a cache_control, at any depth.
    public static int CountCacheBreakpoints(JsonNode? node) => node switch
    {
        JsonObject obj => (obj.ContainsKey("cache_control") ? 1 : 0)
            + obj.Sum(pair => CountCacheBreakpoints(pair.Value)),
        JsonArray array => array.Sum(CountCacheBreakpoints),
        _ => 0,
    };

    public static JsonArray Messages(JsonObject request) => request["messages"]!.AsArray();

    // The content block types of one message, in order.
    public static List<string> BlockTypes(JsonNode message) =>
        message["content"]!.AsArray().Select(block => block!["type"]!.GetValue<string>()).ToList();

    public static JsonObject? Block(JsonObject request, string type) =>
        Messages(request)
            .SelectMany(message => message!["content"] is JsonArray content ? content : [])
            .OfType<JsonObject>()
            .FirstOrDefault(block => block["type"]!.GetValue<string>() == type);

    public static string WithoutCacheControl(JsonNode node)
    {
        var copy = JsonNode.Parse(node.ToJsonString())!;
        Strip(copy);
        return copy.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        static void Strip(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject obj:
                    obj.Remove("cache_control");
                    foreach (var pair in obj.ToList())
                        Strip(pair.Value);
                    break;
                case JsonArray array:
                    foreach (var item in array)
                        Strip(item);
                    break;
            }
        }
    }
}

// A response body that serves its bytes, then blocks the next read until the token is
// cancelled. CreateContentReadStreamAsync hands the stream out at once; the default would
// buffer the whole body first and hang before the reader saw a byte.
internal sealed class HangingContent(byte[] head, CancellationToken hangUntil) : HttpContent
{
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new HangingStream(head, hangUntil));

    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        throw new NotSupportedException();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    private sealed class HangingStream(byte[] head, CancellationToken hangUntil) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < head.Length)
            {
                var count = Math.Min(buffer.Length, head.Length - _position);
                head.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(hangUntil, cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
