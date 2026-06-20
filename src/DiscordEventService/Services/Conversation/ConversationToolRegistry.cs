using System.Diagnostics;
using System.Globalization;
using DiscordEventService.Configuration;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// Builds the per-turn set of tools the agentic loop offers the model (#240). Scoped,
// so the tools it bakes can close over scoped services (MemeSearchService -> the
// request DbContext) — AIFunctionFactory's IServiceProvider binding resolves from the
// wrong scope and [FromKeyedServices] is not honoured, so per-conversation context and
// scoped dependencies are captured in the closure instead. §4+ add more tools here.
internal sealed class ConversationToolRegistry(
    MemeSearchService memeSearch,
    IOptions<ConversationOptions> options,
    ILogger<ConversationToolRegistry> logger)
{
    private const int MaxMemeResults = 10;
    private const int MaxHitDescriptionLength = 160;

    // All-required + no-additional-properties => a clean schema the model can't drift
    // past with junk keys. (Anthropic via OpenRouter ignores the OpenAI `strict` wire
    // flag, but the tightened schema still helps the model and our binding.)
    private static readonly AIJsonSchemaCreateOptions StrictSchema = new AIJsonSchemaCreateOptions
    {
        TransformOptions = new AIJsonSchemaTransformOptions
        {
            DisallowAdditionalProperties = true,
            RequireAllProperties = true,
        },
    };

    public ConversationToolset BuildToolset(ConversationContext context) =>
        new ConversationToolset([BuildMemeSearchTool(context)], logger);

    private AIFunction BuildMemeSearchTool(ConversationContext context) =>
        AIFunctionFactory.Create(
            // CancellationToken is auto-bound by AIFunctionFactory and stays out of the
            // schema; query/limit are the only model-facing parameters.
            (string query, int limit, CancellationToken ct) => SearchMemesAsync(context, query, limit, ct),
            new AIFunctionFactoryOptions
            {
                Name = "meme_search",
                Description =
                    "Search the server's indexed memes and images by description, the text on the image (OCR), "
                    + "tags, or template. Returns a ranked list, each with a jump link, tags, a short description, "
                    + "and the post date. Use it whenever the user wants to find a meme/image or refers to one by "
                    + "its content. `query` is free-text keywords in any language; `limit` is how many results to "
                    + "return (1-10, use 5 unless the user asks for more).",
                JsonSchemaCreateOptions = StrictSchema,
            });

    private async Task<string> SearchMemesAsync(
        ConversationContext context, string query, int limit, CancellationToken cancellationToken)
    {
        var guildId = context.GuildId ?? options.Value.PrimaryGuildId;
        if (guildId is null)
            return "This conversation is not tied to a server, so meme search is unavailable here.";

        if (string.IsNullOrWhiteSpace(query))
            return "Provide a non-empty search query (keywords, text from the image, or a tag).";

        var boundedLimit = Math.Clamp(limit, 1, MaxMemeResults);
        var hits = await memeSearch.SearchAsync(guildId.Value, query, boundedLimit, cancellationToken);
        if (hits.Count == 0)
            return $"No memes matched \"{query}\".";

        return FormatHits(guildId.Value, hits);
    }

    // Model-facing rendering (distinct from /meme's user-facing pills): one numbered
    // line per hit with the jump link plus the metadata the model needs to talk about
    // it. The search itself is MemeSearchService — no duplicated query logic.
    private static string FormatHits(ulong guildId, IReadOnlyList<MemeSearchHit> hits)
    {
        var lines = hits.Select((hit, index) =>
        {
            var link = $"https://discord.com/channels/{guildId}/{hit.ChannelDiscordId}/{hit.MessageDiscordId}";
            return $"{index + 1}. {link} — {DescribeHit(hit)}";
        });
        return $"Found {hits.Count} meme(s):\n{string.Join("\n", lines)}";
    }

    private static string DescribeHit(MemeSearchHit hit)
    {
        List<string> parts = [];
        if (hit.Tags.Length > 0)
            parts.Add("tags: " + string.Join(", ", hit.Tags));

        var description = hit.DescriptionPl ?? hit.DescriptionEn;
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(Truncate(description.ReplaceLineEndings(" "), MaxHitDescriptionLength));

        parts.Add("posted " + hit.MessageCreatedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return string.Join(" | ", parts);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}

// The dispatch seam for one turn: the single place every tool call is invoked, timed,
// logged, and span-traced (the foundation the §5 usage ledger builds on). A failing
// tool is caught here and handed back to the model as an error string — it must never
// throw out of the agentic loop (#240).
internal sealed class ConversationToolset
{
    private const int MaxLoggedArgumentsLength = 200;

    private readonly Dictionary<string, AIFunction> _functions;
    private readonly ILogger _logger;

    public ConversationToolset(IEnumerable<AIFunction> functions, ILogger logger)
    {
        _functions = functions.ToDictionary(function => function.Name);
        _logger = logger;
        Tools = [.. _functions.Values];
    }

    // Re-sent on every round so the model always sees the available tools.
    public IList<AITool> Tools { get; }

    public async Task<FunctionResultContent> InvokeAsync(
        FunctionCallContent call, CancellationToken cancellationToken)
    {
        using var activity = ConversationTelemetry.ActivitySource.StartActivity($"tool {call.Name}");
        var argsText = DescribeArguments(call.Arguments);
        activity?.SetTag("tool.name", call.Name);
        activity?.SetTag("tool.arguments", argsText);

        var stopwatch = Stopwatch.StartNew();
        object? result;
        try
        {
            if (_functions.TryGetValue(call.Name, out var function))
            {
                var arguments = new AIFunctionArguments(call.Arguments ?? new Dictionary<string, object?>());
                result = await function.InvokeAsync(arguments, cancellationToken);
            }
            else
            {
                result = $"Error: unknown tool \"{call.Name}\".";
            }
        }
        catch (OperationCanceledException)
        {
            // The whole turn was cancelled (timeout / shutdown) — let it unwind.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} threw; returning the error to the model", call.Name);
            activity?.SetTag("tool.error", ex.GetType().Name);
            result = $"Error running tool \"{call.Name}\": {ex.Message}";
        }
        stopwatch.Stop();

        var resultText = result?.ToString() ?? string.Empty;
        activity?.SetTag("tool.latency_ms", stopwatch.ElapsedMilliseconds);
        activity?.SetTag("tool.result_length", resultText.Length);
        _logger.LogInformation(
            "Tool {Tool} args={Args} -> {ResultLength} chars in {LatencyMs}ms",
            call.Name, argsText, resultText.Length, stopwatch.ElapsedMilliseconds);

        return new FunctionResultContent(call.CallId, result);
    }

    private static string DescribeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return "{}";

        var joined = string.Join(", ", arguments.Select(pair => $"{pair.Key}={pair.Value}"));
        return joined.Length <= MaxLoggedArgumentsLength ? joined : joined[..(MaxLoggedArgumentsLength - 1)] + "…";
    }
}
