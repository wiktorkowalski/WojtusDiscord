using DiscordEventService.Data;
using DiscordEventService.Services.Pipeline;
using Hangfire;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;

namespace DiscordEventService.Services;

// Missing child-container registrations otherwise surface only at runtime when a handler
// fires — sometimes hours after deploy, often only logged at Error inside a catch block.
internal static class StartupValidator
{
    // Services event handlers resolve via scopeFactory.CreateScope().
    // Constructor-injected transitive dependencies are also validated by trying
    // to construct each entry below. The shared scoped services come from
    // CoreServiceRegistration (single source of truth); the rest are
    // child-container infrastructure registered inline in Program.cs.
    private static readonly Type[] RequiredChildContainerServices =
    [
        .. CoreServiceRegistration.CoreServiceTypes,
        typeof(DiscordDbContext),
        typeof(IBackgroundJobClient),
        typeof(IHostEnvironment),
        typeof(IMemoryCache),
        typeof(EventPipeline),
        typeof(IChatClient),
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
                    failures.Add($"{type.FullName} is not registered");
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
