using DiscordEventService.Commands;
using DiscordEventService.Configuration;
using DiscordEventService.Services.EventHandlers;
using DiscordEventService.Services.Pipeline;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using Hangfire;

namespace DiscordEventService.Services;

// Builds the DSharpPlus client singleton: the child DI container, the full
// event-handler roster, and the slash-command subsystem. Services the child
// container shares with the root come from AddCoreServices() — see
// CoreServiceRegistration.
internal static class DiscordClientRegistration
{
    public static void AddDiscordClient(
        this IServiceCollection rootServices,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<IServiceCollection> addDiscordDbContext)
    {
        var discordToken = configuration.GetSection(DiscordOptions.SectionName).Get<DiscordOptions>()?.Token
            ?? throw new InvalidOperationException("Discord:Token is required");
        var commandGuildId = configuration.GetSection(DiscordOptions.SectionName).Get<DiscordOptions>()?.CommandGuildId;

        rootServices.AddSingleton(rootSp =>
        {
            var clientBuilder = DiscordClientBuilder.CreateDefault(discordToken, DiscordIntents.All);

            ConfigureChildServices(clientBuilder, rootSp, configuration, environment, addDiscordDbContext);
            ConfigureEventHandlers(clientBuilder);
            ConfigureCommands(clientBuilder, commandGuildId);

            return clientBuilder.Build();
        });
    }

    // Register ASP.NET Core services in DSharpPlus's DI container. Services event
    // handlers share with the root container come from AddCoreServices(); add new
    // shared services to CoreServiceRegistration.CoreServiceTypes (one line, both
    // containers + StartupValidator pick them up automatically).
    private static void ConfigureChildServices(
        DiscordClientBuilder clientBuilder,
        IServiceProvider rootSp,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<IServiceCollection> addDiscordDbContext)
    {
        clientBuilder.ConfigureServices(services =>
        {
            addDiscordDbContext(services);

            services.AddSingleton(environment);
            services.AddSingleton<EventPipeline>();
            services.AddCoreServices();
            // #222: MessageEventHandler's live meme-index hook reads the configured
            // meme channels; root-container option bindings aren't visible here.
            services.AddOptions<MemeIndexOptions>()
                .Bind(configuration.GetSection(MemeIndexOptions.SectionName));
            // IBackgroundJobClient forwards to the root container's registration.
            // Hangfire registers IBackgroundJobClient as Transient with DI-based
            // JobStorage resolution; using `new BackgroundJobClient()` directly
            // would read JobStorage.Current (a static set by AddHangfireServer's
            // HostedService at app.Run() time), which is null at validator time
            // post-Build but pre-Run. Forwarding resolves through Hangfire's
            // DI-aware factory which works at any time after Build.
            services.AddSingleton<IBackgroundJobClient>(_ => rootSp.GetRequiredService<IBackgroundJobClient>());
            services.AddMemoryCache();
        });
    }

    private static void ConfigureEventHandlers(DiscordClientBuilder clientBuilder) =>
        clientBuilder.ConfigureEventHandlers(b => b
            .AddEventHandlers<MessageEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<ReactionEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<PollEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<PinEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<VoiceEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<VoiceServerEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<PresenceEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<MemberEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<BanEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<GuildMembersChunkHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<GuildEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<GuildUpdateEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<EmojiEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<StickerEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<ChannelEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<RoleEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<ThreadEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<ThreadSyncHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<StageInstanceEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<ScheduledEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<AutoModEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<AutoModRuleEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<InviteEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<TypingEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<WebhookEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<IntegrationEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<AuditLogEventHandler>(ServiceLifetime.Scoped)
            .AddEventHandlers<SocketLifecycleHandler>(ServiceLifetime.Scoped)
        );

    // #224: the bot's first user-facing command. Slash-only (no text-prefix
    // processor — the bot must keep ignoring message content), guild-scoped
    // registration so it shows up instantly. Without Discord:CommandGuildId
    // the commands subsystem isn't wired at all (pure passive logger).
    private static void ConfigureCommands(DiscordClientBuilder clientBuilder, ulong? commandGuildId)
    {
        if (commandGuildId is { } commandGuild)
        {
            clientBuilder.UseCommands((_, extension) =>
            {
                extension.AddProcessor(new SlashCommandProcessor());
                extension.AddCommands([typeof(MemeCommand)], commandGuild);
            }, new CommandsConfiguration { RegisterDefaultCommandProcessors = false });
        }
    }
}
