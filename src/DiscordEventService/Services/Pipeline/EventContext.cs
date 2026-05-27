using DiscordEventService.Data;

namespace DiscordEventService.Services.Pipeline;

public record EventContext(
    DiscordDbContext Db,
    IServiceProvider Services,
    Guid CorrelationId,
    string? RawJson,
    DateTime ReceivedAtUtc,
    ILogger Logger);
