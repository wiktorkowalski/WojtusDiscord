using DiscordEventService.Jobs;
using DiscordEventService.Services.Conversation;
using DiscordEventService.Services.MemeIndexing;
using DiscordEventService.Services.Pipeline;

namespace DiscordEventService.Services;

// Single source of truth for the scoped services both the root (ASP.NET Core) and the DSharpPlus
// child DI container need — registration and StartupValidator both loop over CoreServiceTypes.
internal static class CoreServiceRegistration
{
    public static readonly Type[] CoreServiceTypes =
    [
        typeof(UserService),
        typeof(GuildUpsertService),
        typeof(ChannelUpsertService),
        typeof(FkResolver),
        typeof(RawEventLogService),
        typeof(FailedEventService),
        typeof(DowntimeTrackerService),
        typeof(GuildBackfillOrchestrator),
        typeof(BootQuickSyncService),
        typeof(MemeSearchService),
        typeof(GuildStatsService),
        typeof(DatabaseQueryService),
        typeof(ConversationToolRegistry),
        typeof(ConversationMemoryService),
        typeof(ConversationService),
        typeof(UsageAlertService),
    ];

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        foreach (var type in CoreServiceTypes)
            services.AddScoped(type);
        return services;
    }
}
