using DiscordEventService.Data;
using DiscordEventService.Data.Entities.Core;
using DiscordEventService.Services.MemeIndexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordEventService.Tests;

// MemeSearchService writes its search log through a scope factory (#384), so a test cannot
// build it from a DbContext alone. One place for that wiring.
internal static class MemeSearchTestServices
{
    // For a test that does not look at the log row.
    public static readonly MemeSearchCaller AnyCaller = new(MemeSearchSource.Other, ChannelDiscordId: 0UL, UserDiscordId: 0UL);

    public static MemeSearchService NewSearch(DiscordDbContext db, string connectionString) =>
        new MemeSearchService(db, NewLogWriter(connectionString));

    // The real writer over its own provider, as in production. Await LastWrite before a read of the log.
    public static MemeSearchLogWriter NewLogWriter(string connectionString, ILogger<MemeSearchLogWriter>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DiscordDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        return new MemeSearchLogWriter(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<MemeSearchLogWriter>.Instance);
    }
}
