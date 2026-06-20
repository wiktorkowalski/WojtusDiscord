using System.ClientModel;
using System.Text;
using DiscordEventService.Configuration;
using DiscordEventService.Infrastructure;
using DSharpPlus;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace DiscordEventService.Services.Conversation;

// Wires the conversational assistant's IChatClient (MEAI over OpenRouter) plus its
// Langfuse OTel export. The IChatClient is a process-wide singleton — the DSharpPlus
// child container forwards to this one instance (like IBackgroundJobClient), so there
// is a single OTel pipeline and HTTP stack. The TracerProvider is root-only.
internal static class ConversationRegistration
{
    // Root container: bind options, build the singleton IChatClient, wire Langfuse.
    public static IServiceCollection AddConversationFeature(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ConversationOptions>()
            .Bind(configuration.GetSection(ConversationOptions.SectionName));

        services.AddSingleton<IChatClient>(CreateChatClient);

        // query_database (#238 §4): the cached schema hint for the tool description, derived from the
        // same SchemaCatalog the dashboard explorer uses (so it tracks the EF model).
        services.AddSingleton(sp => DatabaseSchemaHint.Build(sp.GetRequiredService<SchemaCatalog>()));

        // Action tools (#238 §6): the Discord-write seam + the staged-action store. Singletons —
        // GuildActionService is stateless over the singleton DiscordClient, and ConfirmationService
        // holds pending confirmations in memory across the turn that stages one and the click that
        // confirms it. (Both also registered in the child container, which is where they're used.)
        services.AddSingleton<IGuildActionService, GuildActionService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();

        AddLangfuseTracing(services, configuration);
        return services;
    }

    // DSharpPlus child container: forward the single IChatClient and bind the options
    // the handler/service read (root-container bindings aren't visible here).
    public static void AddConversationChildServices(
        IServiceCollection services, IServiceProvider rootSp, IConfiguration configuration)
    {
        services.AddOptions<ConversationOptions>()
            .Bind(configuration.GetSection(ConversationOptions.SectionName));

        // Mirror the root's OPENROUTER_API_KEY fallback so the in-handler IsConfigured
        // gate agrees with the (root-built) chat client's actual key.
        services.AddOptions<OpenRouterOptions>()
            .Bind(configuration.GetSection(OpenRouterOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                    options.ApiKey = configuration["OPENROUTER_API_KEY"];
            });

        services.AddSingleton<IChatClient>(_ => rootSp.GetRequiredService<IChatClient>());

        // Forward the single cached schema hint so ConversationToolRegistry resolves it in the child
        // container too (DatabaseQueryService/GuildStatsService use the shared DiscordDbContext).
        services.AddSingleton(_ => rootSp.GetRequiredService<DatabaseSchemaHint>());

        // §6: forward the singleton DiscordClient so the action services can perform Discord writes
        // from the child container (the conversation + confirm-button handlers run here). The lambda
        // is lazy — it returns the already-built root singleton, so there's no construction cycle.
        services.AddSingleton(_ => rootSp.GetRequiredService<DiscordClient>());

        // §6 action seam — its own child-container singletons. The ConfirmationService store is the
        // one that matters: staging (during a turn) and the confirm click both run in this child
        // container, so they share this single instance.
        services.AddSingleton<IGuildActionService, GuildActionService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();
    }

    private static IChatClient CreateChatClient(IServiceProvider sp)
    {
        var openRouter = sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
        var conversation = sp.GetRequiredService<IOptions<ConversationOptions>>().Value;
        var environment = sp.GetRequiredService<IHostEnvironment>();

        // A placeholder key keeps construction (and DI validation) from throwing when the
        // bot boots unconfigured; ConversationService gates on IsConfigured before sending.
        var apiKey = string.IsNullOrWhiteSpace(openRouter.ApiKey) ? "unconfigured" : openRouter.ApiKey;
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(openRouter.BaseUrl) });

        return openAiClient
            .GetChatClient(conversation.Model)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: ConversationTelemetry.SourceName,
                configure: client => client.EnableSensitiveData = environment.IsDevelopment())
            .Build();
    }

    private static void AddLangfuseTracing(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ConversationOptions.SectionName).Get<ConversationOptions>()
            ?? new ConversationOptions();
        if (!options.LangfuseConfigured)
            return;

        var endpoint = new Uri($"{options.LangfuseHost!.TrimEnd('/')}/api/public/otel/v1/traces");
        var authorization = "Authorization=Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.LangfusePublicKey}:{options.LangfuseSecretKey}"));

        // HttpProtobuf, not gRPC — Langfuse's OTLP endpoint silently no-ops on gRPC.
        services.AddOpenTelemetry().WithTracing(tracing => tracing
            .AddSource(ConversationTelemetry.SourceName)
            .AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = endpoint;
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporter.Headers = authorization;
            }));
    }
}
