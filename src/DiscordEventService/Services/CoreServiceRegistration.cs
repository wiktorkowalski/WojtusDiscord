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
        typeof(RoleUpsertService),
        typeof(EmoteUpsertService),
        typeof(StickerUpsertService),
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

    // Narrow seams over the scoped services above, forwarded to the same instance so a
    // caller that takes the interface shares the request's DbContext (#308). They exist so
    // the interaction flows are constructible in a plain unit test.
    private static readonly (Type Seam, Type Implementation)[] CoreServiceSeams =
    [
        (typeof(IConversationTurnSource), typeof(ConversationService)),
        (typeof(IUsageAlertService), typeof(UsageAlertService)),
    ];

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        foreach (var type in CoreServiceTypes)
            services.AddScoped(type);

        foreach (var (seam, implementation) in CoreServiceSeams)
            services.AddScoped(seam, sp => sp.GetRequiredService(implementation));

        return services;
    }
}
