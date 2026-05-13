using DiscordEventService.Data;
using DiscordEventService.Jobs;
using Hangfire;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services;

/// <summary>
/// Validates that every service event handlers depend on is registered in the
/// DSharpPlus child DI container. Without this, missing registrations only
/// surface at runtime when the handler actually fires — sometimes hours after
/// deploy, often only logged at Error level inside a handler's catch block.
/// </summary>
public static class StartupValidator
{
    // Services event handlers resolve via scopeFactory.CreateScope().
    // Constructor-injected transitive dependencies are also validated by trying
    // to construct each entry below.
    private static readonly Type[] RequiredChildContainerServices =
    [
        typeof(DiscordDbContext),
        typeof(UserService),
        typeof(RawEventLogService),
        typeof(FailedEventService),
        typeof(DowntimeTrackerService),
        typeof(GuildBackfillOrchestrator),
        typeof(IBackgroundJobClient),
        typeof(IHostEnvironment),
        typeof(IMemoryCache),
    ];

    public static void ValidateChildContainer(IServiceProvider childProvider, ILogger logger)
    {
        using var scope = childProvider.CreateScope();
        var failures = new List<string>();

        foreach (var type in RequiredChildContainerServices)
        {
            try
            {
                var service = scope.ServiceProvider.GetService(type);
                if (service is null)
                {
                    failures.Add($"{type.FullName} is not registered");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{type.FullName} resolved with error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            var message = "DSharpPlus child container DI validation failed:\n  - " + string.Join("\n  - ", failures);
            throw new InvalidOperationException(message);
        }

        logger.LogInformation(
            "DSharpPlus child container DI validation passed ({Count} services resolved)",
            RequiredChildContainerServices.Length);
    }
}
