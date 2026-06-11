using DiscordEventService.Jobs;
using DiscordEventService.Services.MemeIndexing;
using DiscordEventService.Services.Pipeline;

namespace DiscordEventService.Services;

/// <summary>
/// Single source of truth for the scoped services that both the root (ASP.NET Core) and the
/// DSharpPlus child DI container need. Registration loops over <see cref="CoreServiceTypes"/> and
/// <see cref="StartupValidator"/> validates the same list, so adding a service is one line here and
/// it is registered in both containers and validated at startup.
/// </summary>
public static class CoreServiceRegistration
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
    ];

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        foreach (var type in CoreServiceTypes)
            services.AddScoped(type);
        return services;
    }
}
