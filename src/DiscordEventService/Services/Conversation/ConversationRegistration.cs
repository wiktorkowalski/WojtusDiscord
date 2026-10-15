using System.Diagnostics;
using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Core;
using DiscordEventService.Configuration;
using DiscordEventService.Infrastructure;
using DiscordEventService.Services.Conversation.Interaction;
using Microsoft.Extensions.AI;
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
        services.AddOptions<ConversationOptions>()
            .Bind(configuration.GetSection(ConversationOptions.SectionName))
            .ValidateDataAnnotations()
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
            .Use(inner => new CacheUsageTagChatClient(inner))
            .Build();
}

// MEAI's OTel client tags the cache read (gen_ai.usage.cache_read.input_tokens) but not the
// cache write. This adds the OpenTelemetry GenAI name for it to the generation span (#426).
internal sealed class CacheUsageTagChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    internal const string CacheCreationTag = "gen_ai.usage.cache_creation.input_tokens";

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
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usage
                        && usage.Details.AdditionalCounts?.TryGetValue(RoundTokens.CacheWriteKey, out var written) is true)
                        span.SetTag(CacheCreationTag, written);
                }
            }

            yield return update;
        }
    }
}
