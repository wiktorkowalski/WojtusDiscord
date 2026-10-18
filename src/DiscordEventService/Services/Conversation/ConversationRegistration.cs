using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using DiscordEventService.Configuration;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DiscordEventService.Services.Conversation;

// Wires the conversational assistant's IChatClient (MEAI over the Claude API, ADR-0007). The IChatClient
// is a process-wide singleton — the DSharpPlus child container forwards to this one instance
// (like IBackgroundJobClient), so there is a single OTel pipeline and HTTP stack. The
// TracerProvider that exports its spans is root-only (TelemetryRegistration).
internal static class ConversationRegistration
{
    // Root container: bind options, build the singleton IChatClient.
    public static IServiceCollection AddConversationFeature(
        this IServiceCollection services, IConfiguration configuration)
    {
        // A ReasoningEffort the API does not know would be a 400 on every call: fail at boot.
        // An unpriced model writes null costs, which sum as $0 and silence every cost cap (#453).
        services.AddOptions<ConversationOptions>()
            .Bind(configuration.GetSection(ConversationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.Model is not null && o.Prices.ContainsKey(o.Model), "Conversation:Prices has no entry for Conversation:Model")
            .ValidateOnStart();

        AddAnthropicOptions(services, configuration);
        services.AddSingleton<IChatClient>(CreateChatClient);

        // query_database (#238 §4): the cached schema hint for the tool description, derived from the
        // same SchemaCatalog the dashboard explorer uses (so it tracks the EF model).
        services.AddSingleton(sp => DatabaseSchemaHint.Build(sp.GetRequiredService<SchemaCatalog>()));

        // Action tools (#238 §6): the Discord-write seam + the staged-action store. Singletons —
        // GuildActionService is stateless over the singleton DiscordClient, and ConfirmationService
        // holds pending confirmations in memory across the turn that stages one and the click that
        // confirms it. (Both also registered in the child container, which is where they're used.)
        // They reach the DiscordClient via DiscordClientAccessor (a plain holder), NEVER by
        // registering DiscordClient in a container — see DiscordClientAccessor for why that deadlocks.
        services.AddSingleton<DiscordClientAccessor>();
        services.AddSingleton<IGuildActionService, GuildActionService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();

        // §4 live-state read seam (#270): cache-first Discord reads, same accessor rule.
        services.AddSingleton<IGuildLiveStateService, GuildLiveStateService>();

        // §3 cost-cap alert transport (#269): DMs admins via the accessor, same
        // no-DiscordClient-in-DI rule as the action services.
        services.AddSingleton<IUsageAlertNotifier, DiscordUsageAlertNotifier>();

        return services;
    }

    // DSharpPlus child container: forward the single IChatClient and bind the options
    // the handler/service read (root-container bindings aren't visible here).
    public static void AddConversationChildServices(
        IServiceCollection services, IServiceProvider rootSp, IConfiguration configuration)
    {
        services.AddOptions<ConversationOptions>()
            .Bind(configuration.GetSection(ConversationOptions.SectionName));

        // The same binding as the root, so the in-handler IsConfigured gate agrees with the
        // (root-built) chat client's actual key.
        AddAnthropicOptions(services, configuration);

        services.AddSingleton<IChatClient>(_ => rootSp.GetRequiredService<IChatClient>());

        // Forward the single cached schema hint so ConversationToolRegistry resolves it in the child
        // container too (DatabaseQueryService/GuildStatsService use the shared DiscordDbContext).
        services.AddSingleton(_ => rootSp.GetRequiredService<DatabaseSchemaHint>());

        // §6: forward the DiscordClientAccessor (a plain holder) so the action services reach the
        // DiscordClient without DiscordClient ever being a registration in this child container —
        // forwarding DiscordClient itself re-enters its own construction and deadlocks boot.
        services.AddSingleton(_ => rootSp.GetRequiredService<DiscordClientAccessor>());

        // §6 action seam — its own child-container singletons. The ConfirmationService store is the
        // one that matters: staging (during a turn) and the confirm click both run in this child
        // container, so they share this single instance.
        services.AddSingleton<IGuildActionService, GuildActionService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();

        // §4: the scoped ConversationToolRegistry (CoreServiceTypes) resolves the live-state
        // read seam here.
        services.AddSingleton<IGuildLiveStateService, GuildLiveStateService>();

        // §3: the scoped UsageAlertService (CoreServiceTypes) resolves its notifier here.
        services.AddSingleton<IUsageAlertNotifier, DiscordUsageAlertNotifier>();

        // #308: the Discord-free flows the two interaction handlers delegate to. Child
        // container only — they exist to serve event handlers, which the root never runs.
        // Scoped to match the handlers and the scoped seams they resolve.
        services.AddScoped<ConversationFlow>();
        services.AddScoped<ConfirmationFlow>();
    }

    // The Claude API key, boot-safe when unset (the assistant stays inert). ANTHROPIC_API_KEY
    // is accepted as a fallback for Anthropic__ApiKey.
    private static void AddAnthropicOptions(IServiceCollection services, IConfiguration configuration) =>
        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                    options.ApiKey = configuration[AnthropicOptions.ApiKeyEnvironmentVariable];
            });

    // One attempt's limit for the response headers (the SDK timeout ends once the headers
    // arrive; the stream itself runs under the turn's RequestTimeoutSeconds). Well under the
    // turn budget, so a hung attempt still leaves time for a retry.
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);

    private static IChatClient CreateChatClient(IServiceProvider sp)
    {
        var anthropic = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
        var conversation = sp.GetRequiredService<IOptions<ConversationOptions>>().Value;
        var environment = sp.GetRequiredService<IHostEnvironment>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<AnthropicAttemptLogHandler>();

        // A placeholder key keeps construction (and DI validation) from throwing when the
        // bot boots unconfigured; ConversationService gates on IsConfigured before sending.
        var apiKey = anthropic.IsConfigured ? anthropic.ApiKey! : "unconfigured";
        return BuildChatClient(
            CreateApiClient(apiKey, logger), conversation,
            conversation.EnableSensitiveData || environment.IsDevelopment());
    }

    // The base URL is fixed: without it the SDK takes ANTHROPIC_BASE_URL from the process
    // environment, and one stray line in the server .env would send every request, key
    // included, to another host. MaxRetries 0: the §2 per-round policy (#268) is the single
    // owner of retries — it ledgers every attempt and honours Retry-After; the SDK's own
    // silent retries would stack under it and under-count the ledger. The SDK keeps its own
    // HttpClient in production; tests pass a scripted one. A handler binds to one client, so
    // every call builds a new one.
    internal static IAnthropicClient CreateApiClient(string apiKey, ILogger logger, HttpClient? httpClient = null)
    {
        IAnthropicClient client = new AnthropicClient
        {
            ApiKey = apiKey,
            BaseUrl = EnvironmentUrl.Production,
            MaxRetries = 0,
            Timeout = AttemptTimeout,
            Handlers = [new AnthropicAttemptLogHandler(logger)],
        };

        return httpClient is null ? client : client.WithOptions(options => options with { HttpClient = httpClient });
    }

    // Outermost first. The usage tagger sits inside the OTel client, so it runs within the
    // generation span of each call.
    internal static IChatClient BuildChatClient(
        IAnthropicClient client, ConversationOptions conversation, bool enableSensitiveData) =>
        client.AsIChatClient(conversation.Model, conversation.MaxOutputTokens)
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: ConversationTelemetry.SourceName,
                configure: telemetry => telemetry.EnableSensitiveData = enableSensitiveData)
            .Use(inner => new CacheUsageTagChatClient(inner, conversation))
            .Build();
}

// Usage tags on the generation span (#426):
// - MEAI's OTel client tags the cache read (gen_ai.usage.cache_read.input_tokens) but not the
//   cache write. This adds the OpenTelemetry GenAI name for it.
// - Langfuse reads gen_ai.usage.input_tokens (uncached + cache read + cache write) as uncached
//   input, so its total counts the cache twice and its cost prices it at the full rate. The
//   langfuse.observation.* JSON attributes take precedence over gen_ai.* in Langfuse: they carry
//   the uncached input and the ledger's own cost (ConversationCost). Langfuse sums the usage total.
internal sealed class CacheUsageTagChatClient(IChatClient innerClient, ConversationOptions options)
    : DelegatingChatClient(innerClient)
{
    internal const string CacheCreationTag = "gen_ai.usage.cache_creation.input_tokens";
    internal const string LangfuseUsageTag = "langfuse.observation.usage_details";
    internal const string LangfuseCostTag = "langfuse.observation.cost_details";

    private const string CacheReadKey = "cache_read_input_tokens";
    private const string CacheWriteKey = "cache_creation_input_tokens";

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Taken before the first yield: after it the iterator resumes on its consumer's
        // context, where Activity.Current is no longer the generation span.
        var span = Activity.Current is { Source.Name: ConversationTelemetry.SourceName } current ? current : null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (span is not null)
            {
                // One update per stream carries the usage; a plain loop keeps the per-delta path cheap.
                foreach (var content in update.Contents)
                {
                    if (content is not UsageContent usage)
                        continue;
                    if (usage.Details.AdditionalCounts?.TryGetValue(RoundTokens.CacheWriteKey, out var written) is true)
                        span.SetTag(CacheCreationTag, written);
                    TagLangfuseUsage(span, RoundTokens.From([update]));
                    break;
                }
            }

            yield return update;
        }
    }

    private void TagLangfuseUsage(Activity span, RoundTokens tokens)
    {
        if (tokens.UncachedInput is not { } uncached)
            return;

        var usage = new Dictionary<string, long> { ["input"] = uncached, ["output"] = tokens.Output ?? 0 };
        if (tokens.CacheRead is > 0)
            usage[CacheReadKey] = tokens.CacheRead.Value;
        if (tokens.CacheWrite is > 0)
            usage[CacheWriteKey] = tokens.CacheWrite.Value;
        span.SetTag(LangfuseUsageTag, JsonSerializer.Serialize(usage));

        // No price: skip the cost tag before Compute, which would take the warn-once slot with a
        // NullLogger. ConversationService computes the same cost for the ledger and logs the gap.
        if (!options.Prices.ContainsKey(options.Model)
            || ConversationCost.Compute(options, tokens, NullLogger.Instance) is not { } cost)
            return;

        var costs = new Dictionary<string, double> { ["input"] = cost.InputUsd, ["output"] = cost.OutputUsd };
        if (usage.ContainsKey(CacheReadKey))
            costs[CacheReadKey] = cost.CacheReadUsd;
        if (usage.ContainsKey(CacheWriteKey))
            costs[CacheWriteKey] = cost.CacheWriteUsd;
        if (tokens.WebSearches is > 0)
            costs["web_search"] = cost.WebSearchUsd;
        costs["total"] = cost.TotalUsd;
        span.SetTag(LangfuseCostTag, JsonSerializer.Serialize(costs));
    }
}
