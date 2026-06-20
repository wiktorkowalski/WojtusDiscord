using System.ClientModel;
using System.Text;
using DiscordEventService.Configuration;
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
